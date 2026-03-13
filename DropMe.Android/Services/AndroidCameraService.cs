using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Util;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using DropMe.Services;
using Java.Util.Concurrent;

namespace DropMe.Android.Services;

public sealed class AndroidCameraService : ICameraService {
    private ProcessCameraProvider? _cameraProvider;
    private ImageAnalysis? _analysis;
    private CancellationTokenSource? _loopCts;
    private IExecutorService? _analyzerExecutor;

    private bool _useFrontCamera;

    public event Action<CameraFrame>? FrameArrived;

    public int SelectedCameraIndex => _useFrontCamera ? 1 : 0;

    public IReadOnlyList<string> GetAvailableCameras() =>
        new[] { "Back Camera", "Front Camera" };

    public async Task<bool> SelectCameraAsync(int index, CancellationToken ct = default) {
        var useFront = index == 1;
        if (_useFrontCamera == useFront)
            return true;

        _useFrontCamera = useFront;

        if (_cameraProvider is not null) {
            var activity = MainActivity.CurrentActivity
                ?? throw new InvalidOperationException("No Android activity available.");
            await BindCameraAsync(activity).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> ToggleCameraAsync(CancellationToken ct = default) {
        _useFrontCamera = !_useFrontCamera;

        if (_cameraProvider is not null) {
            var activity = MainActivity.CurrentActivity
                ?? throw new InvalidOperationException("No Android activity available.");
            await BindCameraAsync(activity).ConfigureAwait(false);
        }

        return true;
    }

    public async Task StartAsync(CancellationToken ct = default) {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var activity = MainActivity.CurrentActivity
            ?? throw new InvalidOperationException("No Android activity available.");

        var provider = await GetCameraProviderAsync(activity).ConfigureAwait(false);
        _cameraProvider = provider;

        _analyzerExecutor = Executors.NewSingleThreadExecutor();

        _analysis = new ImageAnalysis.Builder()
            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
            .SetOutputImageFormat(ImageAnalysis.OutputImageFormatRgba8888)
            .Build();

        _analysis.SetAnalyzer(_analyzerExecutor, new Analyzer(image => {
            if (_loopCts?.IsCancellationRequested == true) {
                image.Close();
                return;
            }

            try {
                var planes = image.GetPlanes();
                if (planes is null || planes.Length == 0) {
                    image.Close();
                    return;
                }

                var plane = planes[0];
                var buffer = plane.Buffer;
                var size = buffer.Remaining();

                var bytes = new byte[size];
                buffer.Get(bytes);

                FrameArrived?.Invoke(new CameraFrame(
                    image.Width,
                    image.Height,
                    bytes,
                    plane.RowStride,
                    image.ImageInfo.RotationDegrees));
            }
            finally {
                image.Close();
            }
        }));

        await BindCameraAsync(activity).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct = default) {
        _loopCts?.Cancel();
        _loopCts = null;

        _analysis?.ClearAnalyzer();
        _analysis = null;

        _analyzerExecutor?.Shutdown();
        _analyzerExecutor = null;

        var activity = MainActivity.CurrentActivity;
        if (activity is not null && _cameraProvider is not null) {
            activity.RunOnUiThread(() => _cameraProvider.UnbindAll());
        }

        _cameraProvider = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task BindCameraAsync(global::Android.App.Activity activity) {
        if (_cameraProvider is null || _analysis is null)
            return;

        var selector = _useFrontCamera
            ? CameraSelector.DefaultFrontCamera
            : CameraSelector.DefaultBackCamera;

        await RunOnUiThreadAsync(activity, () => {
            _cameraProvider.UnbindAll();
            _cameraProvider.BindToLifecycle((ILifecycleOwner)activity, selector, _analysis);
        }).ConfigureAwait(false);
    }

    private static Task<ProcessCameraProvider> GetCameraProviderAsync(Context context) {
        var future = ProcessCameraProvider.GetInstance(context);
        var tcs = new TaskCompletionSource<ProcessCameraProvider>();

        future.AddListener(new Java.Lang.Runnable(() => {
            try {
                tcs.TrySetResult((ProcessCameraProvider)future.Get());
            }
            catch (Exception ex) {
                tcs.TrySetException(ex);
            }
        }), ContextCompat.GetMainExecutor(context));

        return tcs.Task;
    }

    private static Task RunOnUiThreadAsync(global::Android.App.Activity activity, Action action) {
        var tcs = new TaskCompletionSource<bool>();
        activity.RunOnUiThread(() => {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    private sealed class Analyzer(Action<IImageProxy> onFrame) : Java.Lang.Object, ImageAnalysis.IAnalyzer {
        public Size? DefaultTargetResolution => null;
        public void Analyze(IImageProxy image) => onFrame(image);
    }
}