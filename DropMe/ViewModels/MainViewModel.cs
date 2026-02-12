using System;
using System.IO;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DropMe.Services;
using DropMe.Services.Session;

namespace DropMe.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged {
    private readonly ICameraService _camera;
    private readonly QrDecoder _decoder;
    private readonly IQrCodeService _qr;
    private byte[]? _latestFrameBytes;
    private int _latestStride;
    private int _latestWidth;
    private int _latestHeight;
    private readonly object _frameLock = new();
    private int _previewFrameCounter = 0;
    private DispatcherTimer? _renderTimer;
    private ISession? _session;
    private CancellationTokenSource? _sessionCts;
    private int _qrHandled = 0;
    private readonly SessionFactory _sessionFactory;
    private readonly IDeviceService _device;


    public bool IsConnected => _session?.State == SessionState.Connected;





    private CancellationTokenSource? _scanCts;

    private Bitmap? _qrImage;
    public Bitmap? QrImage {
        get => _qrImage;
        private set { _qrImage = value; OnPropertyChanged(); }
    }

    private WriteableBitmap? _preview;
    public WriteableBitmap? Preview {
        get => _preview;
        private set {
            _preview = value;
            OnPropertyChanged();
        }
    }


    private string? _decodedText;
    public string? DecodedText {
        get => _decodedText;
        private set { _decodedText = value; OnPropertyChanged(); }
    }

    private string? _status;
    public string? Status {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }
    public MainViewModel(
        ICameraService camera,
        QrDecoder decoder,
        IQrCodeService qr,
        SessionFactory sessionFactory,
        IDeviceService device)
    {
        _camera = camera;
        _decoder = decoder;
        _qr = qr;
        _sessionFactory = sessionFactory;
        _device = device;

        _camera.FrameArrived += OnFrameArrived;
        Status = "Ready";

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => RenderPreview();
        _renderTimer.Start();
    }


    
    public void GenerateQr()
    {
        const int port = 5050;
        _ = StartHostingAsync(port);

        var psk = new byte[32];
        RandomNumberGenerator.Fill(psk);

        var ip = _device.GetLocalLanIp();

        var invite = new ConnectionInvite(
            V: 1,
            Ip: ip,
            Port: port,
            Sid: Guid.NewGuid().ToString("N"),
            Psk: ConnectionInviteCodec.Base64UrlEncode(psk)
        );

        var text = ConnectionInviteCodec.Encode(invite);
        QrImage = _qr.Generate(text, pixelsPerModule: 10);
    }


    private async Task StartHostingAsync(int port)
    {
        try
        {
            _sessionCts?.Cancel();
            _sessionCts = new CancellationTokenSource();

            _session = new TcpHostSession(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
            Status = "Waiting for peer…";

            await _session.StartAsync(_sessionCts.Token);

            Status = $"Connected to {_session.Peer}";
            OnPropertyChanged(nameof(IsConnected));
        }
        catch (Exception ex)
        {
            Status = $"Host error: {ex.Message}";
        }
    }


    public async Task StartScanAsync() {
        _renderTimer?.Start();

        if (_scanCts is not null) return;

        _scanCts = new CancellationTokenSource();
        Status = "Starting camera...";
        DecodedText = null;

        await _camera.StartAsync(_scanCts.Token);
        Status = "Scanning... show a QR to the camera.";
    }

    public async Task StopScanAsync() {
        _renderTimer?.Stop();

        if (_scanCts is null) return;

        _scanCts.Cancel();
        _scanCts.Dispose();
        _scanCts = null;

        await _camera.StopAsync();
        Status = "Scan stopped.";

    }

    private DateTime _lastUiFrame = DateTime.MinValue;
    private DateTime _lastDecode = DateTime.MinValue;

    private void OnFrameArrived(CameraFrame frame) {
        lock (_frameLock) {
            _latestWidth = frame.Width;
            _latestHeight = frame.Height;
            _latestStride = frame.Stride;

            int needed = frame.Stride * frame.Height;
            if (_latestFrameBytes == null || _latestFrameBytes.Length != needed)
                _latestFrameBytes = new byte[needed];

            Buffer.BlockCopy(frame.Rgba, 0, _latestFrameBytes, 0, needed);
        }
    }

    private void RenderPreview()
    {
        byte[]? bytes;
        int w, h, stride;

        lock (_frameLock)
        {
            bytes = _latestFrameBytes;
            w = _latestWidth;
            h = _latestHeight;
            stride = _latestStride;
        }

        if (bytes is null || w <= 0 || h <= 0)
            return;

        // Create a fresh bitmap each tick (PoC reliable)
        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using (var fb = bmp.Lock())
        {
            int rows = Math.Min(h, fb.Size.Height);
            int dstStride = fb.RowBytes;

            if (stride == dstStride)
            {
                Marshal.Copy(bytes, 0, fb.Address, rows * dstStride);
            }
            else
            {
                int colsBytes = Math.Min(stride, dstStride);
                for (int y = 0; y < rows; y++)
                {
                    Marshal.Copy(bytes, y * stride, fb.Address + (y * dstStride), colsBytes);
                }
            }
        }

        // Assign NEW reference => Image will redraw, guaranteed
        Preview = bmp;

        var now = DateTime.UtcNow;
        if ((now - _lastDecode).TotalMilliseconds >= 200) // 5x/sec
        {
            _lastDecode = now;

            // Use the latest frame bytes (already captured)
            CameraFrame? snapshot = null;
            lock (_frameLock) {
                if (_latestFrameBytes is not null)
                    snapshot = new CameraFrame(_latestWidth, _latestHeight, (byte[])_latestFrameBytes.Clone(), _latestStride);
            }

            if (snapshot is not null) {
                var decoded = _decoder.TryDecode(snapshot);
                if (!string.IsNullOrWhiteSpace(decoded)) {
                    DecodedText = decoded;
                    Status = "QR decoded";
                    if (Interlocked.Exchange(ref _qrHandled, 1) == 1)
                        return;
                    _ = HandleQrDecodedAsync(decoded); // fire-and-forget async workflow
                }

            }
        }

    }




    private void EnsurePreviewBitmap(int width, int height) {
        if (Preview is not null && Preview.PixelSize.Width == width && Preview.PixelSize.Height == height)
            return;

        Preview = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
    }
    private async Task HandleQrDecodedAsync(string decoded) {
        try {
            await StopScanAsync();

            Status = "Parsing invite…";


            if (!ConnectionInviteCodec.TryDecode(decoded, out var invite) || invite is null) {
                Status = "Invalid invite";
                return;
            }

            Status = "Connecting to peer…";

            _sessionCts = new CancellationTokenSource();
            _session = _sessionFactory.Create(invite);

            await _session.StartAsync(_sessionCts.Token);

            Status = $"Connected to {invite.Ip}:{invite.Port}";
            OnPropertyChanged(nameof(IsConnected));
        }
        catch (Exception ex) {
            Status = $"Connection failed: {ex.Message}";
        }
    }

    private void CopyBytesToPreview(byte[] rgba, int width, int height, int srcStride) {
        if (Preview is null) return;

        using (var fb = Preview.Lock()) {
            int rows = Math.Min(height, fb.Size.Height);
            int dstStride = fb.RowBytes;

            if (srcStride == dstStride) {
                Marshal.Copy(rgba, 0, fb.Address, rows * dstStride);
            }
            else {
                int colsBytes = Math.Min(srcStride, dstStride);
                for (int y = 0; y < rows; y++) {
                    Marshal.Copy(
                        rgba,
                        y * srcStride,
                        fb.Address + (y * dstStride),
                        colsBytes);
                }
            }
        }

        //  FORCE AVALONIA TO REBIND THE IMAGE SOURCE
        if (++_previewFrameCounter % 5 == 0) {
            Preview = Preview;
        }
    }
    public async Task SendFileAsync()
    {
        if (_session is null)
            return;

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "helloworld.txt");

        // PoC: ensure file exists
        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(
                path,
                "Hello world from DropMe 👋\n");
        }

        Status = "Sending file…";
        await _session.SendFileAsync(path, CancellationToken.None);
        Status = "File sent.";
    }





    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}
