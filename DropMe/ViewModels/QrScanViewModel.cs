using System;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DropMe.Services;
using DropMe.Services.Session;

namespace DropMe.ViewModels;

public sealed class QrScanViewModel {
    private readonly ICameraService _camera;
    private readonly QrDecoder _decoder;

    private DateTime _lastDecode = DateTime.MinValue;

    public WriteableBitmap? Preview { get; private set; }
    public string? LastDecodedText { get; private set; }
    public ConnectionInvite? LastInvite { get; private set; }

    public QrScanViewModel(ICameraService camera, QrDecoder decoder) {
        _camera = camera;
        _decoder = decoder;

        _camera.FrameArrived += OnFrame;
    }

    public async void Start() {
        await _camera.StartAsync();
    }

    public async void Stop() {
        await _camera.StopAsync();
    }

    private void OnFrame(CameraFrame frame) {
        // Throttle decode (e.g., 5x/sec) to keep CPU reasonable
        var now = DateTime.UtcNow;
        if ((now - _lastDecode).TotalMilliseconds < 200)
            return;

        _lastDecode = now;

        // Decode on background thread (we are already in background from camera service)
        var decoded = _decoder.TryDecode(frame);
        if (decoded is null) return;

        if (ConnectionInviteCodec.TryDecode(decoded, out var invite) && invite is not null) {
            LastInvite = invite;
        }

        LastDecodedText = decoded;

        // If want to display preview frames, update WriteableBitmap here
        Dispatcher.UIThread.Post(() => {
            // Raise property changed implementing INotifyPropertyChanged
            // For PoC just log:
            System.Diagnostics.Debug.WriteLine($"QR: {LastDecodedText}");
        });
    }
}