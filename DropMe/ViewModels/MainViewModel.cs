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
using System.Collections.Generic;
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
    private byte[]? _scanFrameBytes;
    private int _latestStride;
    private int _latestWidth;
    private int _latestHeight;
    private readonly object _frameLock = new();
    private int _latestFrameVersion;
    private int _renderedFrameVersion = -1;
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

    private string? _lastGeneratedInviteText;
    private readonly List<string> _availableCameras = new();
    private int _selectedCameraIndex;

    public bool IsConnected => _sessionManager.IsConnected;
    public bool IsScanning => _scanCts is not null;
    public bool ShowGeneratedQr => !IsScanning;
    public string MainCardTitle => IsScanning ? "Camera Preview" : "Your QR Code";
    public string ScanButtonText => IsScanning ? "Stop scanning" : "Scan QR";
    public IReadOnlyList<string> AvailableCameras => _availableCameras;
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

    public int SelectedCameraIndex {
        get => _selectedCameraIndex;
        set {
            if (_selectedCameraIndex == value) return;
            _selectedCameraIndex = value;
            OnPropertyChanged();
            _ = SelectCameraByIndexAsync(value);
        }
    }

    public bool CanToggleCamera => _availableCameras.Count > 1;
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
        RefreshCameraList();
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

    private void RefreshCameraList() {
        _availableCameras.Clear();
        foreach (var name in _camera.GetAvailableCameras())
            _availableCameras.Add(name);

        if (_availableCameras.Count == 0)
            _availableCameras.Add("Camera 0");

        _selectedCameraIndex = Math.Clamp(_camera.SelectedCameraIndex, 0, _availableCameras.Count - 1);
        OnPropertyChanged(nameof(AvailableCameras));
        OnPropertyChanged(nameof(SelectedCameraIndex));
        OnPropertyChanged(nameof(CanToggleCamera));
    }

    public async Task ToggleCameraAsync() {
        var ok = await _camera.ToggleCameraAsync();
        if (ok) {
            RefreshCameraList();
        }
    }

    public async Task SelectCameraByIndexAsync(int index) {
        var ok = await _camera.SelectCameraAsync(index);
        if (ok) {
            RefreshCameraList();
        }
    }


    public void GenerateQr() {
        try {
            var aesKey = new byte[32];
            RandomNumberGenerator.Fill(aesKey);

            var ip = _device.GetLocalLanIp();
            var port = ReserveAvailableTcpPort();
            var btInfo = GetBluetoothInfoOrNull();

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

            _ = Task.Run(async () => {
                var btPermissions = await Task.Run(async () => {
                    if (!_permissionsService.HasBluetoothPermissions) {
                        await _permissionsService.RequestBluetoothPermission();
                    }

                    await _permissionsService.RequestBluetoothDiscoverablePermission(300);
                    return _permissionsService is { HasBluetoothPermissions: true, HasBluetoothDiscoverablePermissions: true };
                });

                BtConnectionInfo? btConnInfo = null;
                if (btPermissions) {
                    var btInfo = _device.GetLocalBluetoothInfo();
                    if (btInfo is var (address, name)) {
                        btConnInfo = new BtConnectionInfo(address?.ToString(), name);
                    }
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
        ClearCameraPreviewState();
        _renderTimer?.Start();
        Status = "Starting camera...";
        DecodedText = null;
        HomeSessionMessage = null;
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(ShowGeneratedQr));
        OnPropertyChanged(nameof(MainCardTitle));
        OnPropertyChanged(nameof(ScanButtonText));

        try {
            RefreshCameraList();
            await _camera.StartAsync(_scanCts.Token);
            Status = "Scanning... show a QR to the camera.";
        }
        catch (Exception ex) {
            _scanCts.Dispose();
            _scanCts = null;
            Status = $"Camera error: {ex.Message}";
            _renderTimer?.Stop();
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(ShowGeneratedQr));
            OnPropertyChanged(nameof(MainCardTitle));
            OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    public async Task StopScanAsync() {
        _renderTimer?.Stop();

        if (_scanCts is null) return;

        _scanCts.Cancel();
        _scanCts.Dispose();
        _scanCts = null;

        try {
            await _camera.StopAsync();
            Status = "Scan stopped.";
            ClearCameraPreviewState();
        }
        catch (Exception ex) {
            Status = $"Camera stop error: {ex.Message}";
            ClearCameraPreviewState();
        }
        finally {
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(ShowGeneratedQr));
            OnPropertyChanged(nameof(MainCardTitle));
            OnPropertyChanged(nameof(ScanButtonText));
        }

    }

    private void ClearCameraPreviewState() {
        Preview?.Dispose();
        Preview = null;
        _lastDecode = DateTime.MinValue;

        lock (_frameLock) {
            _latestFrameBytes = null;
            _scanFrameBytes = null;
            _latestStride = 0;
            _latestWidth = 0;
            _latestHeight = 0;
            _latestFrameVersion = 0;
            _renderedFrameVersion = -1;
        }
    }

    private DateTime _lastDecode = DateTime.MinValue;

    private void OnFrameArrived(CameraFrame frame) {
        lock (_frameLock) {
            var rotation = NormalizeRotation(frame.Rotation);
            var targetWidth = rotation is 90 or 270 ? frame.Height : frame.Width;
            var targetHeight = rotation is 90 or 270 ? frame.Width : frame.Height;
            var targetStride = targetWidth * 4;
            var needed = targetStride * targetHeight;

            if (_latestFrameBytes == null || _latestFrameBytes.Length != needed)
                _latestFrameBytes = new byte[needed];

            CopyFrameToPreviewBuffer(frame, _latestFrameBytes, targetWidth, targetHeight, targetStride, rotation);

            _latestWidth = targetWidth;
            _latestHeight = targetHeight;
            _latestStride = targetStride;
            _latestFrameVersion++;
        }
    }

    private void RenderPreview() {
        int w, h, stride, frameVersion;

        lock (_frameLock) {
            w = _latestWidth;
            h = _latestHeight;
            stride = _latestStride;
            frameVersion = _latestFrameVersion;
        }

        if (w <= 0 || h <= 0)
            return;

        if (frameVersion == _renderedFrameVersion && Preview is not null)
            return;

        var isAndroid = OperatingSystem.IsAndroid();
        var pixelFormat = isAndroid
            ? PixelFormat.Rgba8888
            : PixelFormat.Bgra8888;

        var bmp = EnsurePreviewBitmap(w, h, pixelFormat);

        var scanWindow = ComputeScanWindow(w, h);
        using (var fb = bmp.Lock()) {
            lock (_frameLock) {
                if (_latestFrameBytes is null)
                    return;

                w = _latestWidth;
                h = _latestHeight;
                stride = _latestStride;
                frameVersion = _latestFrameVersion;

                if (frameVersion == _renderedFrameVersion)
                    return;

                int rows = Math.Min(h, fb.Size.Height);
                int dstStride = fb.RowBytes;

                if (stride == dstStride) {
                    Marshal.Copy(_latestFrameBytes, 0, fb.Address, rows * dstStride);
                }
                else {
                    int colsBytes = Math.Min(stride, dstStride);
                    for (int y = 0; y < rows; y++) {
                        Marshal.Copy(_latestFrameBytes, y * stride, fb.Address + (y * dstStride), colsBytes);
                    }
                }
            }
        }

        Preview = bmp;
        _renderedFrameVersion = frameVersion;

        var now = DateTime.UtcNow;
        if ((now - _lastDecode).TotalMilliseconds >= 100) {
            _lastDecode = now;
            CameraFrame? snapshot = null;
            lock (_frameLock) {
                if (_latestFrameBytes is not null) {
                    snapshot = CropForScan(
                        _latestWidth,
                        _latestHeight,
                        _latestStride,
                        _latestFrameBytes,
                        (scanWindow.decodeX, scanWindow.decodeY, scanWindow.decodeW, scanWindow.decodeH));
                }
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

    private (int x, int y, int targetW, int targetH, int scanX, int scanY, int scanW, int scanH) ComputeScanArea(
        int frameWidth,
        int frameHeight,
        int sideFractionPercent,
        int featherFractionPercent) {
        var side = (int)(Math.Min(frameWidth, frameHeight) * (sideFractionPercent / 100.0));
        var featherPercent = featherFractionPercent / 100.0;

        var targetW = Math.Max(180, side);
        var targetH = Math.Max(180, side);

        var x = Math.Max(0, (frameWidth - targetW) / 2);
        var y = Math.Max(0, (frameHeight - targetH) / 2);
        var feather = (int)(Math.Min(targetW, targetH) * featherPercent);

        var scanX = Math.Max(0, x - feather);
        var scanY = Math.Max(0, y - feather);
        var scanW = Math.Min(frameWidth - scanX, targetW + feather * 2);
        var scanH = Math.Min(frameHeight - scanY, targetH + feather * 2);
        return (x, y, targetW, targetH, scanX, scanY, scanW, scanH);
    }

    private (int boxX, int boxY, int boxW, int boxH, int decodeX, int decodeY, int decodeW, int decodeH) ComputeScanWindow(
        int frameWidth,
        int frameHeight) {
        var area = ComputeScanArea(frameWidth, frameHeight, 62, 16);
        return (area.x, area.y, area.targetW, area.targetH, area.scanX, area.scanY, area.scanW, area.scanH);
    }

    private CameraFrame CropForScan(
        int frameWidth,
        int frameHeight,
        int stride,
        byte[] sourceBytes,
        (int x, int y, int width, int height) scanArea) {
        var (x, y, width, height) = scanArea;
        var safeWidth = Math.Max(1, Math.Min(width, frameWidth - x));
        var safeHeight = Math.Max(1, Math.Min(height, frameHeight - y));

        var bytesPerPixel = 4;
        var requiredBytes = safeWidth * safeHeight * bytesPerPixel;
        if (_scanFrameBytes is null || _scanFrameBytes.Length != requiredBytes)
            _scanFrameBytes = new byte[requiredBytes];

        var sourceOffsetBase = y * stride + x * bytesPerPixel;
        var destStride = safeWidth * bytesPerPixel;

        for (var row = 0; row < safeHeight; row++) {
            var sourceOffset = sourceOffsetBase + row * stride;
            var destOffset = row * destStride;
            Buffer.BlockCopy(sourceBytes, sourceOffset, _scanFrameBytes, destOffset, destStride);
        }

        return new CameraFrame(safeWidth, safeHeight, _scanFrameBytes, destStride, 0);
    }

    private WriteableBitmap EnsurePreviewBitmap(int width, int height, PixelFormat pixelFormat) {
        if (Preview is not null && Preview.PixelSize.Width == width && Preview.PixelSize.Height == height)
            return Preview;

        Preview?.Dispose();
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            pixelFormat,
            AlphaFormat.Unpremul);
        Preview = bitmap;
        return bitmap;
    }

    private static int NormalizeRotation(int rotationDegrees) {
        var normalized = rotationDegrees % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized switch {
            0 or 90 or 180 or 270 => normalized,
            _ => 0
        };
    }

    private static void CopyFrameToPreviewBuffer(
        CameraFrame frame,
        byte[] destination,
        int targetWidth,
        int targetHeight,
        int targetStride,
        int rotation) {
        var source = frame.Rgba;
        if (rotation == 0) {
            var copyBytesPerRow = frame.Width * 4;
            for (var row = 0; row < frame.Height; row++) {
                Buffer.BlockCopy(source, row * frame.Stride, destination, row * targetStride, copyBytesPerRow);
            }
            return;
        }

        for (var sourceY = 0; sourceY < frame.Height; sourceY++) {
            var sourceRow = sourceY * frame.Stride;
            for (var sourceX = 0; sourceX < frame.Width; sourceX++) {
                var sourceOffset = sourceRow + (sourceX * 4);
                var (targetX, targetY) = rotation switch {
                    90 => (frame.Height - 1 - sourceY, sourceX),
                    180 => (frame.Width - 1 - sourceX, frame.Height - 1 - sourceY),
                    270 => (sourceY, frame.Width - 1 - sourceX),
                    _ => (sourceX, sourceY)
                };

                if ((uint)targetX >= (uint)targetWidth || (uint)targetY >= (uint)targetHeight)
                    continue;

                var targetOffset = (targetY * targetStride) + (targetX * 4);
                destination[targetOffset] = source[sourceOffset];
                destination[targetOffset + 1] = source[sourceOffset + 1];
                destination[targetOffset + 2] = source[sourceOffset + 2];
                destination[targetOffset + 3] = source[sourceOffset + 3];
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
