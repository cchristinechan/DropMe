using System;
using System.IO;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
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
    // Keep true while testing two app instances on the same computer.
    // Set to false later to block same-machine loopback connections.
    private const bool AllowSameMachineConnectionsForTesting = true;

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
    private CancellationTokenSource? _sessionMonitorCts;
    private bool _isStoppingSession;
    private int _qrHandled = 0;
    private readonly SessionFactory _sessionFactory;
    private readonly IDeviceService _device;
    private IStorageService _storageService;
    private string? _lastGeneratedInviteText;
    private string? _localInviteSessionId;


    public bool IsConnected => _session?.State == SessionState.Connected;

    public bool IsScanning => _scanCts is not null;
    public bool ShowGeneratedQr => !IsScanning;
    public string MainCardTitle => IsScanning ? "Camera Preview" : "Your QR Code";
    public string ScanButtonText => IsScanning ? "Stop scanning" : "Scan QR";

    private string? _sessionId;
    public string? SessionId {
        get => _sessionId;
        private set { _sessionId = value; OnPropertyChanged(); }
    }

    private string _sessionStatus = "Not connected";
    public string SessionStatus {
        get => _sessionStatus;
        private set { _sessionStatus = value; OnPropertyChanged(); }
    }

    private string? _sessionMessage;
    public string? SessionMessage {
        get => _sessionMessage;
        private set { _sessionMessage = value; OnPropertyChanged(); }
    }

    private string? _homeSessionMessage;
    public string? HomeSessionMessage {
        get => _homeSessionMessage;
        private set { _homeSessionMessage = value; OnPropertyChanged(); }
    }

    public event Action? SessionConnected;
    public event Action? SessionEnded;

    public Func<FileOfferInfo, System.Threading.Tasks.Task<bool>>? FileOfferDecisionUi;
    // Opens a file picker, lets user choose file, and opens a stream for reading to it
    public Func<Task<(string, Stream)?>>? PickFileStreamUi;
    public Func<Task<string?>>? PickDownloadFolderUi;

    public sealed record FileOfferInfo(Guid FileId, string Name, long Size);

    private TaskCompletionSource<bool>? _pendingFileOfferTcs;

    private bool _hasPendingFileOffer;
    public bool HasPendingFileOffer {
        get => _hasPendingFileOffer;
        private set { _hasPendingFileOffer = value; OnPropertyChanged(); }
    }

    private string? _pendingFileOfferMessage;
    public string? PendingFileOfferMessage {
        get => _pendingFileOfferMessage;
        private set { _pendingFileOfferMessage = value; OnPropertyChanged(); }
    }

    private string? _pendingFileOfferName;
    public string? PendingFileOfferName {
        get => _pendingFileOfferName;
        private set { _pendingFileOfferName = value; OnPropertyChanged(); }
    }

    private string? _pendingFileOfferSizeText;
    public string? PendingFileOfferSizeText {
        get => _pendingFileOfferSizeText;
        private set { _pendingFileOfferSizeText = value; OnPropertyChanged(); }
    }

    private string _downloadFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "DropMeReceived");
    public string DownloadFolder {
        get => _downloadFolder;
        private set { _downloadFolder = value; OnPropertyChanged(); }
    }


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
        IDeviceService device) {
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



    public void GenerateQr() {
        try {
            var psk = new byte[32];
            RandomNumberGenerator.Fill(psk);

            var ip = _device.GetLocalLanIp();
            var port = ReserveAvailableTcpPort();

            var invite = new ConnectionInvite(
                V: 2,
                Ip: ip,
                Port: port,
                Sid: Guid.NewGuid().ToString("N"),
                Psk: ConnectionInviteCodec.Base64UrlEncode(psk),
                Transport: "lan",
                Ssid: null,
                Bssid: null
            );

            SessionId = invite.Sid;
            _localInviteSessionId = invite.Sid;

            var text = ConnectionInviteCodec.Encode(invite);
            _lastGeneratedInviteText = text;
            var bmp = _qr.Generate(text, pixelsPerModule: 10);

            Dispatcher.UIThread.Post(() => {
                QrImage = bmp;
            });

            _ = StartHostingAsync(port);
        }
        catch (Exception ex) {
            Status = $"QR error: {ex.Message}";
        }
    }

    private static int ReserveAvailableTcpPort() {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }


    private async Task StartHostingAsync(int port) {
        try {
            _sessionMonitorCts?.Cancel();
            _sessionMonitorCts?.Dispose();
            _sessionMonitorCts = null;

            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();

            if (_session is not null) {
                try {
                    await _session.StopAsync();
                }
                catch {
                    // Best effort: old session may already be closed/canceled.
                }

                _session = null;
            }

            _session = new TcpHostSession(_storageService, new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));

            if (_session is TcpHostSession h) {
                h.FileSaved += path =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Received and saved: {path}");

                h.FileAcked += (_, sha) =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Delivered ✅ SHA256={sha}");

                h.FileOfferDecision = async offer => {
                    var info = new FileOfferInfo(offer.FileId, offer.Name, offer.Size);
                    return FileOfferDecisionUi is null ? true : await FileOfferDecisionUi(info);
                };
            }

            ApplyDownloadFolderToSession(_session);
            await _session.StartAsync(_sessionCts.Token);
            StartSessionMonitor(_session, _sessionCts.Token);

            Status = $"Connected to {_session.Peer}";
            SessionStatus = "Connected";
            HomeSessionMessage = null;
            Dispatcher.UIThread.Post(() => SessionConnected?.Invoke());

            OnPropertyChanged(nameof(IsConnected));
        }
        catch (Exception ex) {
            Status = $"Host error: {ex.Message}";
        }
    }


    public async Task StartScanAsync() {
        _renderTimer?.Start();

        if (_scanCts is not null) return;

        _scanCts = new CancellationTokenSource();
        Status = "Starting camera...";
        DecodedText = null;
        HomeSessionMessage = null;
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(ShowGeneratedQr));
        OnPropertyChanged(nameof(MainCardTitle));
        OnPropertyChanged(nameof(ScanButtonText));

        try {
            await _camera.StartAsync(_scanCts.Token);
            Status = "Scanning... show a QR to the camera.";
        }
        catch (Exception ex) {
            Status = $"Camera error: {ex.Message}";
        }
    }

    public async Task StopScanAsync() {
        _renderTimer?.Stop();

        if (_scanCts is null) return;

        _scanCts.Cancel();
        _scanCts.Dispose();
        _scanCts = null;

        await _camera.StopAsync();
        Status = "Scan stopped.";
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(ShowGeneratedQr));
        OnPropertyChanged(nameof(MainCardTitle));
        OnPropertyChanged(nameof(ScanButtonText));

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

    private void RenderPreview() {
        byte[]? bytes;
        int w, h, stride;

        lock (_frameLock) {
            bytes = _latestFrameBytes;
            w = _latestWidth;
            h = _latestHeight;
            stride = _latestStride;
        }

        if (bytes is null || w <= 0 || h <= 0)
            return;

        // Create a fresh bitmap each tick (PoC reliable)
        var pixelFormat = OperatingSystem.IsAndroid()
            ? PixelFormat.Rgba8888
            : PixelFormat.Bgra8888;

        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            pixelFormat,
            AlphaFormat.Unpremul);

        using (var fb = bmp.Lock()) {
            int rows = Math.Min(h, fb.Size.Height);
            int dstStride = fb.RowBytes;

            if (stride == dstStride) {
                Marshal.Copy(bytes, 0, fb.Address, rows * dstStride);
            }
            else {
                int colsBytes = Math.Min(stride, dstStride);
                for (int y = 0; y < rows; y++) {
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
            if (string.Equals(decoded, _lastGeneratedInviteText, StringComparison.Ordinal)) {
                Status = "Ignored your own QR code.";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            Status = "Parsing invite…";

            if (!ConnectionInviteCodec.TryDecode(decoded, out var invite) || invite is null) {
                Status = "Invalid invite";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            if (!AllowSameMachineConnectionsForTesting &&
                string.Equals(invite.Ip, _device.GetLocalLanIp(), StringComparison.OrdinalIgnoreCase)) {
                Status = "Blocked: this QR points to your own computer.";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_localInviteSessionId) &&
                string.Equals(invite.Sid, _localInviteSessionId, StringComparison.Ordinal)) {
                Status = "Ignored your own QR code.";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            await StopScanAsync();
            SessionId = invite.Sid;

            Status = "Connecting to peer…";

            _sessionCts = new CancellationTokenSource();
            _session = _sessionFactory.Create(_storageService, invite);

            if (_session is TcpAesGcmSession s) {
                s.FileSaved += path =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Received and saved: {path}");

                s.FileAcked += (_, sha) =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Delivered ✅ SHA256={sha}");

                s.FileOfferDecision = async offer => {
                    var info = new FileOfferInfo(offer.FileId, offer.Name, offer.Size);
                    return FileOfferDecisionUi is null ? true : await FileOfferDecisionUi(info);
                };
            }
            else if (_session is TcpHostSession h) {
                h.FileSaved += path =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Received and saved: {path}");

                h.FileAcked += (_, sha) =>
                    Dispatcher.UIThread.Post(() => SessionMessage = $"Delivered ✅ SHA256={sha}");

                h.FileOfferDecision = async offer => {
                    var info = new FileOfferInfo(offer.FileId, offer.Name, offer.Size);
                    return FileOfferDecisionUi is null ? true : await FileOfferDecisionUi(info);
                };
            }

            ApplyDownloadFolderToSession(_session);
            await _session.StartAsync(_sessionCts.Token);
            StartSessionMonitor(_session, _sessionCts.Token);

            Status = $"Connected to {invite.Ip}:{invite.Port}";
            SessionStatus = "Connected";
            HomeSessionMessage = null;
            Dispatcher.UIThread.Post(() => SessionConnected?.Invoke());

            OnPropertyChanged(nameof(IsConnected));
        }
        catch (Exception ex) {
            Status = $"Connection failed: {ex.Message}";
            Interlocked.Exchange(ref _qrHandled, 0);
        }
    }

    public async Task StopSessionAsync(bool resetToReady = true, string? homeMessage = null) {
        try {
            _isStoppingSession = true;
            RejectPendingFileOffer();
            _sessionMonitorCts?.Cancel();
            _sessionMonitorCts?.Dispose();
            _sessionMonitorCts = null;

            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;

            if (_session is not null) {
                await _session.StopAsync();
                _session = null;
            }

            SessionId = null;
            SessionStatus = "Not connected";
            SessionMessage = null;
            if (resetToReady)
                Status = "Ready";
            if (!string.IsNullOrWhiteSpace(homeMessage))
                HomeSessionMessage = homeMessage;

            OnPropertyChanged(nameof(IsConnected));
            SessionEnded?.Invoke();
        }
        catch (Exception ex) {
            Status = $"End session error: {ex.Message}";
        }
        finally {
            _isStoppingSession = false;
        }
    }

    public async Task PrepareMainPageAsync(string? homeMessage = null, bool regenerateQr = true) {
        if (IsScanning)
            await StopScanAsync();

        Interlocked.Exchange(ref _qrHandled, 0);
        Preview = null;

        if (regenerateQr)
            GenerateQr();

        if (!string.IsNullOrWhiteSpace(homeMessage))
            HomeSessionMessage = homeMessage;

        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(ShowGeneratedQr));
        OnPropertyChanged(nameof(MainCardTitle));
        OnPropertyChanged(nameof(ScanButtonText));
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
    public async Task SendFileAsync() {
        if (_session is null)
            return;

        try {
            var file = PickFileStreamUi is null
                ? null
                : await PickFileStreamUi();

            if (file is var (filename, filestream)) {
                Status = "Sending file…";
                await _session.SendFileAsync(filestream, filename, CancellationToken.None);
                Status = "File sent.";
            }
            else {
                Status = "Send canceled.";
                return;
            }
        }
        catch (Exception ex) {
            SessionMessage = $"Send failed: {ex.Message}";
            Status = "Send failed.";
        }
    }

    public async Task ChooseDownloadFolderAsync() {
        try {
            var selected = PickDownloadFolderUi is null
                ? null
                : await PickDownloadFolderUi();

            if (string.IsNullOrWhiteSpace(selected)) {
                Status = "Download folder unchanged.";
                return;
            }

            DownloadFolder = selected;
            ApplyDownloadFolderToSession(_session);
            Status = "Download folder updated.";
        }
        catch (Exception ex) {
            Status = $"Folder selection failed: {ex.Message}";
        }
    }

    private void ApplyDownloadFolderToSession(ISession? session) {
        throw new Exception("FIX ME");
        /*
        if (session is TcpAesGcmSession aes)
            aes.DownloadDirectory = DownloadFolder;
        else if (session is TcpHostSession host)
            host.DownloadDirectory = DownloadFolder;*/
    }

    public Task<bool> RequestFileOfferDecisionAsync(FileOfferInfo info) {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref _pendingFileOfferTcs, tcs);
        previous?.TrySetResult(false);

        var sizeKb = Math.Max(1, (int)Math.Ceiling(info.Size / 1024d));
        Dispatcher.UIThread.Post(() => {
            PendingFileOfferName = info.Name;
            PendingFileOfferSizeText = $"{sizeKb} KB";
            PendingFileOfferMessage = "Incoming file offer";
            HasPendingFileOffer = true;
        });

        return tcs.Task;
    }

    public void AcceptPendingFileOffer() {
        ResolvePendingFileOffer(true);
    }

    public void RejectPendingFileOffer() {
        ResolvePendingFileOffer(false);
    }

    private void ResolvePendingFileOffer(bool accepted) {
        var tcs = Interlocked.Exchange(ref _pendingFileOfferTcs, null);
        tcs?.TrySetResult(accepted);

        Dispatcher.UIThread.Post(() => {
            HasPendingFileOffer = false;
            PendingFileOfferMessage = null;
            PendingFileOfferName = null;
            PendingFileOfferSizeText = null;
        });
    }

    private void StartSessionMonitor(ISession session, CancellationToken sessionToken) {
        _sessionMonitorCts?.Cancel();
        _sessionMonitorCts?.Dispose();
        _sessionMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);

        var monitorToken = _sessionMonitorCts.Token;
        _ = Task.Run(async () => {
            try {
                while (!monitorToken.IsCancellationRequested) {
                    var state = session.State;
                    if (state is SessionState.Closed or SessionState.Error) {
                        if (_isStoppingSession)
                            return;

                        Dispatcher.UIThread.Post(async () => {
                            if (_isStoppingSession)
                                return;

                            await StopSessionAsync(
                                resetToReady: false,
                                homeMessage: "Session disconnected.");
                            await PrepareMainPageAsync(
                                homeMessage: "Session disconnected.",
                                regenerateQr: true);
                            Status = "Session ended: peer disconnected.";
                        });
                        return;
                    }

                    await Task.Delay(250, monitorToken);
                }
            }
            catch (OperationCanceledException) { }
        }, monitorToken);
    }





    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}
