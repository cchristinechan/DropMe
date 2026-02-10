using System;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services;

public interface ICameraService : IAsyncDisposable {
    event Action<CameraFrame>? FrameArrived;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}