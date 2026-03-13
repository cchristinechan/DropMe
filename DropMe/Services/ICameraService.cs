using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services;

public interface ICameraService : IAsyncDisposable {
    event Action<CameraFrame>? FrameArrived;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    IReadOnlyList<string> GetAvailableCameras();
    int SelectedCameraIndex { get; }

    Task<bool> SelectCameraAsync(int index, CancellationToken ct = default);
    Task<bool> ToggleCameraAsync(CancellationToken ct = default);
}