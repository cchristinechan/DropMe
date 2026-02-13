using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services;
using OpenCvSharp;

namespace DropMe.Desktop.Services;

public sealed class DesktopCameraService : ICameraService {
    private VideoCapture? _cap;
    private CancellationTokenSource? _loopCts;

    public event Action<CameraFrame>? FrameArrived;

    public Task StartAsync(CancellationToken ct = default) {
        _cap = new VideoCapture(0);


        if (!_cap.IsOpened())
            throw new InvalidOperationException("Could not open camera 0.");
        _cap.Set(VideoCaptureProperties.FrameWidth, 640);
        _cap.Set(VideoCaptureProperties.FrameHeight, 480);
        _cap.Set(VideoCaptureProperties.Fps, 30);
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => Loop(_loopCts.Token), _loopCts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) {
        _loopCts?.Cancel();
        _loopCts = null;

        _cap?.Release();
        _cap?.Dispose();
        _cap = null;

        return Task.CompletedTask;
    }
    private void Loop(CancellationToken ct) {
        try {
            using var mat = new Mat();
            int n = 0;

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

                FrameArrived?.Invoke(new CameraFrame(width, height, bytes, stride));

                if ((++n % 30) == 0)
                    System.Diagnostics.Debug.WriteLine($"Camera frames: {n} ({width}x{height})");

                Thread.Sleep(1);
            }
        }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Camera loop crashed: " + ex);
        }
    }


    public async ValueTask DisposeAsync() => await StopAsync();
}