using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Util;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Core.Content;
using AndroidX.Core.Util;
using AndroidX.Lifecycle;
using DropMe.Services;

namespace DropMe.Android.Services;

public sealed class AndroidCameraService : ICameraService {
    private ProcessCameraProvider? _cameraProvider;
    private ImageAnalysis? _analysis;
    private CancellationTokenSource? _loopCts;

    public event Action<CameraFrame>? FrameArrived;

    public async Task StartAsync(CancellationToken ct = default) {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var activity = MainActivity.CurrentActivity
            ?? throw new InvalidOperationException("No Android activity available.");

        var provider = await GetCameraProviderAsync(activity).ConfigureAwait(false);
        _cameraProvider = provider;

        var analysisBuilder = new ImageAnalysis.Builder()
            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
            .SetOutputImageFormat(ImageAnalysis.OutputImageFormatRgba8888);

        _analysis = analysisBuilder.Build();

        _analysis.SetAnalyzer(ContextCompat.GetMainExecutor(activity), new Analyzer(image => {
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

                var width = image.Width;
                var height = image.Height;
                var stride = plane.RowStride;

                FrameArrived?.Invoke(new CameraFrame(width, height, bytes, stride));
            }
            finally {
                image.Close();
            }
        }));

        var cameraSelector = CameraSelector.DefaultBackCamera;

        await RunOnUiThreadAsync(activity, () => {
            provider.UnbindAll();
            provider.BindToLifecycle((ILifecycleOwner)activity, cameraSelector, _analysis);
        }).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken ct = default) {
        _loopCts?.Cancel();
        _loopCts = null;

        _analysis?.ClearAnalyzer();
        _analysis = null;

        var activity = MainActivity.CurrentActivity;
        if (activity is not null && _cameraProvider is not null) {
            activity.RunOnUiThread(() => _cameraProvider.UnbindAll());
        }

        _cameraProvider = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

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

        public void Analyze(IImageProxy image) {
            onFrame(image);
        }
    }
}
