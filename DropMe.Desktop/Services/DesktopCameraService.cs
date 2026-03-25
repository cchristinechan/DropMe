using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services;
using OpenCvSharp;

namespace DropMe.Desktop.Services;

public sealed class DesktopCameraService : ICameraService {
    private VideoCapture? _cap;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly List<string> _cameraNames = new();

    public event Action<CameraFrame>? FrameArrived;

    public int SelectedCameraIndex { get; private set; } = 0;

    public IReadOnlyList<string> GetAvailableCameras() {
        RefreshCameraList();
        return _cameraNames;
    }

    public async Task<bool> SelectCameraAsync(int index, CancellationToken ct = default) {
        RefreshCameraList();
        if (index < 0 || index >= _cameraNames.Count)
            return false;

        if (SelectedCameraIndex == index)
            return true;

        var wasRunning = _cap is not null;
        if (wasRunning)
            await StopAsync(ct).ConfigureAwait(false);

        SelectedCameraIndex = index;

        if (wasRunning)
            await StartAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> ToggleCameraAsync(CancellationToken ct = default) {
        RefreshCameraList();
        if (_cameraNames.Count <= 1)
            return false;

        var next = (SelectedCameraIndex + 1) % _cameraNames.Count;
        return await SelectCameraAsync(next, ct).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken ct = default) {
        if (_cap is not null)
            await StopAsync(ct).ConfigureAwait(false);

        _cap = new VideoCapture(SelectedCameraIndex);

        if (!_cap.IsOpened())
            throw new InvalidOperationException($"Could not open camera {SelectedCameraIndex}.");

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => Loop(_loopCts.Token), _loopCts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default) {
        var loopTask = _loopTask;
        _loopTask = null;

        _loopCts?.Cancel();
        _loopCts = null;

        if (loopTask is not null) {
            try {
                await loopTask.WaitAsync(ct);
            }
            catch (OperationCanceledException) {
                // Expected when camera scanning is cancelled.
            }
            catch (Exception) {
                // Background loop exceptions are intentionally ignored after shutdown.
            }
        }

        _cap?.Release();
        _cap?.Dispose();
        _cap = null;
    }

    private void RefreshCameraList() {
        _cameraNames.Clear();
        for (int i = 0; i < 10; i++) {
            using var test = new VideoCapture(i);
            if (test.IsOpened()) {
                _cameraNames.Add($"Camera {i}");
            }
        }

        if (_cameraNames.Count == 0) {
            _cameraNames.Add("Camera 0");
        }

        if (SelectedCameraIndex >= _cameraNames.Count)
            SelectedCameraIndex = 0;
    }

    private void Loop(CancellationToken ct) {
        try {
            using var mat = new Mat();

            while (!ct.IsCancellationRequested && _cap is not null) {
                if (!_cap.Read(mat) || mat.Empty())
                    continue;

                using var rgba = new Mat();
                Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2BGRA);

                int width = rgba.Width;
                int height = rgba.Height;
                int stride = (int)rgba.Step();

                var bytes = new byte[stride * height];
                Marshal.Copy(rgba.Data, bytes, 0, bytes.Length);

                FrameArrived?.Invoke(new CameraFrame(width, height, bytes, stride, 0));
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Camera loop crashed: " + ex);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
