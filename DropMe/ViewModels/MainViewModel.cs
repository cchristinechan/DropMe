using System;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
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
using InTheHand.Net;
using InTheHand.Net.Sockets;

namespace DropMe.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable {
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
    private SessionManager _sessionManager;
    private CancellationTokenSource _sessionListenCts = new();
    private CancellationTokenSource _sessionEstablishCts = new();
    private bool _isStoppingSession;
    private int _qrHandled = 0;
    private readonly IDeviceService _device;
    private readonly IStorageService _storageService;
    private readonly IPermissionsService _permissionsService;
    private QrCodeData? _lastGeneratedQrCodeData;
    private Guid? _localInviteSessionId;


    public bool IsConnected => _sessionManager.IsConnected;
    public bool IsTcpConnected => _sessionManager.TcpConnected;
    public bool IsBtConnected => _sessionManager.BtConnected;

    // // DEBUG
    // private bool _debugTcpOverride = false;
    // private bool _debugBtOverride = false;
    //
    // public bool IsTcpConnected => _debugTcpOverride || _sessionManager.TcpConnected;
    // public bool IsBtConnected  => _debugBtOverride  || _sessionManager.BtConnected;
    //
    // public void DebugToggleTcp() {
    //     _debugTcpOverride = !_debugTcpOverride;
    //     OnPropertyChanged(nameof(IsTcpConnected));
    // }
    //
    // public void DebugToggleBt() {
    //     _debugBtOverride = !_debugBtOverride;
    //     OnPropertyChanged(nameof(IsBtConnected));
    // }
    // //

    public bool IsScanning => _scanCts is not null;
    public bool ShowGeneratedQr => !IsScanning;
    public string MainCardTitle => IsScanning ? "Camera Preview" : "Your QR Code";
    public string ScanButtonText => IsScanning ? "Stop scanning" : "Scan QR";

    private bool _isDarkTheme;
    public bool IsDarkTheme {
        get => _isDarkTheme;
        private set {
            _isDarkTheme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeModePillText));
        }
    }

    public string ThemeModePillText => IsDarkTheme ? "Dark" : "Light";

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
        private set {
            _sessionMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSessionMessage));
        }
    }

    public bool HasSessionMessage => !string.IsNullOrWhiteSpace(SessionMessage);

    private string? _homeSessionMessage;
    public string? HomeSessionMessage {
        get => _homeSessionMessage;
        private set {
            _homeSessionMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHomeSessionMessage));
        }
    }

    private string? _downloadFolder = null;
    public string? DownloadFolder {
        get => _downloadFolder;
        private set { _downloadFolder = value; OnPropertyChanged(); }
    }
    public bool HasHomeSessionMessage => !string.IsNullOrWhiteSpace(HomeSessionMessage);

    public event Action? SessionConnected;
    public event Action? SessionEnded;

    public Func<FileOfferInfo, System.Threading.Tasks.Task<bool>>? FileOfferDecisionUi;
    // Opens a file picker, lets user choose file, and opens a stream for reading to it
    public Func<Task<(string, Stream)?>>? PickFileStreamUi;
    public Func<Task>? PickDownloadFolderUi;

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
        IDeviceService device,
        IStorageService storageService,
        IPermissionsService permissionsService) {
        _camera = camera;
        _decoder = decoder;
        _qr = qr;
        _device = device;
        _storageService = storageService;
        _permissionsService = permissionsService;

        _sessionManager = SessionManagerFactory();

        _camera.FrameArrived += OnFrameArrived;
        Status = "Ready";

        DownloadFolder = _storageService.GetDownloadDirectoryLabel();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => RenderPreview();
        _renderTimer.Start();
    }

    private SessionManager SessionManagerFactory() {
        var sessionManager = new SessionManager(_storageService);

        // Give the session manager callbacks
        sessionManager.FileSaved += path =>
            Dispatcher.UIThread.Post(() => SessionMessage = $"Received and saved: {path}");

        sessionManager.FileAcked += (_, sha) =>
            Dispatcher.UIThread.Post(() => SessionMessage = $"Delivered : SHA256={sha}");

        sessionManager.FileOfferDecision = async offer => {
            var info = new FileOfferInfo(offer.FileId, offer.Name, offer.Size);
            return FileOfferDecisionUi is null || await FileOfferDecisionUi(info);
        };

        sessionManager.SessionEnded += reason => {
            Dispatcher.UIThread.Post(async () => {
                if (_isStoppingSession)
                    return;

                await StopSessionAsync(
                    homeMessage: $"Session disconnected {reason.ToString()}.");
                await PrepareMainPageAsync(
                    homeMessage: $"Session disconnected {reason.ToString()}.",
                    regenerateQr: true);
                Status = $"Session ended: peer disconnected {reason.ToString()}.";
            });
        };
        return sessionManager;
    }

    public void GenerateQr() {
        try {
            var aesKey = new byte[32];
            RandomNumberGenerator.Fill(aesKey);

            var ip = _device.GetLocalLanIp();
            var port = ReserveAvailableTcpPort();
            var btInfo = GetBluetoothInfoOrNull();


            _ = Task.Run(async () => {
                var btPermissions = await Task.Run(async () => {
                    if (!_permissionsService.HasBluetoothPermissions) {
                        await _permissionsService.RequestBluetoothPermission();
                    }

                    await _permissionsService.RequestBluetoothDiscoverablePermission(300);
                    return _permissionsService is { HasBluetoothPermissions: true, HasBluetoothDiscoverablePermissions: true };
                });

                BtConnectionInfo? btConnInfo = null;
                if (btInfo is var (address, name)) {
                    btConnInfo = new BtConnectionInfo(address?.ToString(), name);
                }
                var invite = new QrCodeData(
                    V: 2,
                    Sid: Guid.NewGuid(),
                    LanInfo: new LanConnectionInfo(ip, port),
                    BtInfo: btConnInfo,
                    AesKey: aesKey
                );

                _lastGeneratedQrCodeData = invite;

                SessionId = invite.Sid.ToString("N");
                _localInviteSessionId = invite.Sid;
                Console.WriteLine("Trying to gen text");
                var text = QrCodeDataCodec.Encode(invite);
                Console.WriteLine($"Qr code text: {text}");
                var bmp = _qr.Generate(text, pixelsPerModule: 10);

                Dispatcher.UIThread.Post(() => {
                    QrImage = bmp;
                });

                await StartHostingAsync(port, btConnInfo is not null, aesKey);
            });
        }
        catch (Exception ex) {
            Status = $"QR error: {ex.Message}";
        }
    }

    private (BluetoothAddress? address, string name)? GetBluetoothInfoOrNull() {
        try {
            return _device.GetLocalBluetoothInfo();
        }
        catch (Exception ex) {
            Console.WriteLine($"Skipping bluetooth in QR invite due probe failure: {ex.Message}");
            return null;
        }
    }

    private static int ReserveAvailableTcpPort() {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }


    private async Task StartHostingAsync(int port, bool btEnabled, byte[] aesKey) {
        Debug.Assert(aesKey.Length == 32, "AES key must be 32 bytes");
        _sessionManager.AesSessionKey = aesKey;
        try {
            if (btEnabled) {
                await _sessionManager.ListenTcpAndBt(new IPEndPoint(IPAddress.Any, port), _sessionListenCts.Token)
                    .ConfigureAwait(false);
            }
            else {
                await _sessionManager.ListenTcp(new IPEndPoint(IPAddress.Any, port), _sessionListenCts.Token)
                    .ConfigureAwait(false);
            }

            Status = $"Connected to {_sessionManager.PeerName}";
            SessionStatus = "Connected";
            HomeSessionMessage = null;
            Dispatcher.UIThread.Post(() => SessionConnected?.Invoke());

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsTcpConnected));
            OnPropertyChanged(nameof(IsBtConnected));
            await _sessionManager.StartReceiveLoop();
        }
        catch (Exception ex) {
            Status = $"Host error: {ex.Message}";
        }
        finally {
            _sessionManager.Dispose();
            _sessionManager = SessionManagerFactory();
        }

    }


    public async Task StartScanAsync() {
        if (!_permissionsService.HasCameraPermissions) {
            await _permissionsService.RequestCameraPermission();
        }
        // HANDLE DENIAL
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
                var decodedText = _decoder.TryDecode(snapshot);
                if (!string.IsNullOrWhiteSpace(decodedText)) {
                    if (QrCodeDataCodec.TryDecode(decodedText, out var qrCodeData)) {
                        DecodedText = decodedText;
                        Status = "QR decoded";
                        if (Interlocked.Exchange(ref _qrHandled, 1) == 1)
                            return;
                        _ = HandleQrDecodedAsync(qrCodeData!); // fire-and-forget async workflow, qr code data not null as checked trydecode return value
                    }
                    else {
                        Status = "Invalid QR code data";
                    }
                }

            }
        }

    }

    private async Task HandleQrDecodedAsync(QrCodeData qrCodeData) {
        try {
            if (qrCodeData == _lastGeneratedQrCodeData) {
                Status = "Ignored your own QR code.";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            if (_localInviteSessionId is not null && qrCodeData.Sid == _localInviteSessionId) {
                Status = "Ignored your own QR code.";
                Interlocked.Exchange(ref _qrHandled, 0);
                return;
            }

            await StopScanAsync();
            SessionId = qrCodeData.Sid.ToString();

            Status = "Connecting to peer…";

            BluetoothAddress? btAddr = null;
            string? btName = null;
            // No point asking for bluetooth permissions if the server isn't offering bluetooth
            if (qrCodeData.BtInfo is not null && !_permissionsService.HasBluetoothPermissions) {
                await _permissionsService.RequestBluetoothPermission();
            }

            // Only attempt to connect over bluetooth if we have permissions
            if (qrCodeData.BtInfo is var (btAddrStr1, btNameStr1) && _permissionsService.HasBluetoothPermissions) {
                btName = btNameStr1;
                BluetoothAddress.TryParse(btAddrStr1, out btAddr); // Fine not checking output as null is valid
            }

            _sessionManager.AesSessionKey = qrCodeData.AesKey;

            IPEndPoint? lanEp = null;
            if (qrCodeData.LanInfo is var (lanAddrStr, lanPort)) {
                var lanAddr = IPAddress.Parse(lanAddrStr);
                lanEp = new IPEndPoint(lanAddr, lanPort);
            }

            await _sessionManager.EstablishConnections(lanEp, btAddr, btName, _sessionEstablishCts.Token);

            Status = $"Connected to {_sessionManager.PeerName}";
            SessionStatus = "Connected";
            HomeSessionMessage = null;
            Dispatcher.UIThread.Post(() => SessionConnected?.Invoke());

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsTcpConnected));
            OnPropertyChanged(nameof(IsBtConnected));
            await _sessionManager.StartReceiveLoop();

            _sessionManager.Dispose();
            _sessionManager = SessionManagerFactory();
        }
        catch (Exception ex) {
            Status = $"Connection failed: {ex.Message}";
            Interlocked.Exchange(ref _qrHandled, 0);
        }
    }

    public async Task StopSessionAsync(string? homeMessage = null) {
        try {
            _isStoppingSession = true;
            RejectPendingFileOffer();

            await _sessionManager.StopSession();

            SessionId = null;
            SessionStatus = "Not connected";
            SessionMessage = null;
            Status = "Ready";
            if (!string.IsNullOrWhiteSpace(homeMessage))
                HomeSessionMessage = homeMessage;

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsTcpConnected));
            OnPropertyChanged(nameof(IsBtConnected));
            SessionEnded?.Invoke();
        }
        catch (Exception ex) {
            Status = $"End session error: {ex.Message}";
        }
        finally {
            _sessionManager = SessionManagerFactory();
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

    public async Task SendFileAsync() {
        try {
            var file = PickFileStreamUi is null
                ? null
                : await PickFileStreamUi();

            if (file is var (filename, filestream)) {
                Status = "Sending file…";
                await _sessionManager.SendFileAsync(filestream, filename, CancellationToken.None);
                Status = "File sent.";
            }
            else {
                Status = "Send canceled.";
            }
        }
        catch (Exception ex) {
            SessionMessage = $"Send failed: {ex.Message}";
            Status = "Send failed.";
        }
    }

    public async Task ChooseDownloadFolderAsync() {
        if (PickDownloadFolderUi is not null) {
            await PickDownloadFolderUi();
        }
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

    public async Task DoPickDownloadsFolder(Visual? visual) {
        await _storageService.PickDownloadsFolderAsync(visual);
        DownloadFolder = _storageService.GetDownloadDirectoryLabel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() {
        _sessionManager.Dispose();
    }
}
