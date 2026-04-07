using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Android.Util;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using DropMe.Services;
using Java.Util.Concurrent;

namespace DropMe.Android.Services;

public sealed class AndroidCameraService : ICameraService {
    private const int TargetAnalysisWidth = 640;
    private const int TargetAnalysisHeight = 480;
    private ProcessCameraProvider? _cameraProvider;
    private ImageAnalysis? _analysis;
    private Preview? _preview;
    private PreviewView? _previewView;
    private FrameLayout? _previewContainer;
    private CancellationTokenSource? _loopCts;
    private IExecutorService? _analyzerExecutor;
    private byte[]? _frameBuffer;

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

        var analyzerExecutor = Executors.NewSingleThreadExecutor();
        _analyzerExecutor = analyzerExecutor;

        _analysis = new ImageAnalysis.Builder()
            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
            .SetOutputImageFormat(ImageAnalysis.OutputImageFormatRgba8888)
            .Build();
        _preview = new Preview.Builder().Build();

        _analysis.SetAnalyzer(analyzerExecutor, new Analyzer(image => {
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
                buffer.Rewind();
                var size = buffer.Remaining();

                if (_frameBuffer is null || _frameBuffer.Length != size)
                    _frameBuffer = new byte[size];

                buffer.Get(_frameBuffer, 0, size);

                FrameArrived?.Invoke(new CameraFrame(
                    image.Width,
                    image.Height,
                    _frameBuffer,
                    plane.RowStride,
                    NormalizeRotation(image.ImageInfo.RotationDegrees)));
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
        _preview = null;

        _analyzerExecutor?.Shutdown();
        _analyzerExecutor = null;
        _frameBuffer = null;

        var activity = MainActivity.CurrentActivity;
        if (activity is not null && _cameraProvider is not null) {
            activity.RunOnUiThread(() => _cameraProvider.UnbindAll());
        }

        _cameraProvider = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task BindCameraAsync(global::Android.App.Activity activity) {
        if (_cameraProvider is null || _analysis is null || _preview is null)
            return;

        var selector = _useFrontCamera
            ? CameraSelector.DefaultFrontCamera
            : CameraSelector.DefaultBackCamera;

        await RunOnUiThreadAsync(activity, () => {
            if (_previewView is not null) {
                _preview.SetSurfaceProvider(_previewView.SurfaceProvider);
            }

            _cameraProvider.UnbindAll();
            _cameraProvider.BindToLifecycle((ILifecycleOwner)activity, selector, _preview, _analysis);
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

    private static int NormalizeRotation(int rotationDegrees) {
        var normalized = rotationDegrees % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized;
    }

    public View GetNativePreviewView(Context context) {
        EnsureNativePreviewContainer(context);

        if (_previewContainer?.Parent is ViewGroup parent) {
            parent.RemoveView(_previewContainer);
        }

        if (_preview is not null && _previewView is not null) {
            _preview.SetSurfaceProvider(_previewView.SurfaceProvider);
        }

        return _previewContainer ?? throw new InvalidOperationException("Native preview container is not available.");
    }

    private void EnsureNativePreviewContainer(Context context) {
        if (_previewContainer is not null && _previewView is not null)
            return;

        _previewView ??= new PreviewView(context);
        _previewView.SetImplementationMode(PreviewView.ImplementationMode.Performance);
        _previewView.SetScaleType(PreviewView.ScaleType.FillCenter);

        var container = new FrameLayout(context);
        container.SetClipChildren(true);
        container.SetClipToPadding(true);

        container.SetBackgroundColor(Color.Transparent);
        container.AddView(_previewView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var overlay = new ScanOverlayView(context);
        container.AddView(overlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var flipButton = new ImageButton(context);
        flipButton.SetImageResource(global::Android.Resource.Drawable.IcMenuRotate);
        flipButton.SetBackgroundColor(Color.Transparent);
        flipButton.SetColorFilter(GetAvailableCameras().Count > 1 ? Color.White : Color.Rgb(143, 160, 175));
        flipButton.Enabled = GetAvailableCameras().Count > 1;
        flipButton.Alpha = flipButton.Enabled ? 1f : 0.75f;
        flipButton.Click += async (_, _) => {
            if (flipButton.Enabled)
                await ToggleCameraAsync().ConfigureAwait(false);
        };

        var buttonSize = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 40, context.Resources?.DisplayMetrics);
        var buttonMargin = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, 6, context.Resources?.DisplayMetrics);
        var buttonLayout = new FrameLayout.LayoutParams(buttonSize, buttonSize, GravityFlags.Top | GravityFlags.Right) {
            TopMargin = buttonMargin,
            RightMargin = buttonMargin
        };
        container.AddView(flipButton, buttonLayout);

        _previewContainer = container;
    }

    private sealed class Analyzer(Action<IImageProxy> onFrame) : Java.Lang.Object, ImageAnalysis.IAnalyzer {
        public Size? DefaultTargetResolution => new(TargetAnalysisWidth, TargetAnalysisHeight);
        public void Analyze(IImageProxy image) => onFrame(image);
    }

    private sealed class ScanOverlayView(Context context) : View(context) {
        private readonly Paint _maskPaint = new() {
            Color = Color.Argb(166, 0, 0, 0)
        };

        private readonly Paint _framePaint = new() {
            Color = Color.Rgb(85, 255, 170),
            StrokeWidth = TypedValue.ApplyDimension(ComplexUnitType.Dip, 3, context.Resources?.DisplayMetrics),
            AntiAlias = true
        };

        protected override void OnDraw(Canvas canvas) {
            base.OnDraw(canvas);

            var width = Width;
            var height = Height;
            if (width <= 0 || height <= 0)
                return;

            var left = width / 7f;
            var top = height / 7f;
            var right = width * 6f / 7f;
            var bottom = height * 6f / 7f;

            canvas.DrawRect(0, 0, width, top, _maskPaint);
            canvas.DrawRect(0, top, left, bottom, _maskPaint);
            canvas.DrawRect(right, top, width, bottom, _maskPaint);
            canvas.DrawRect(0, bottom, width, height, _maskPaint);

            _framePaint.SetStyle(Paint.Style.Stroke);
            canvas.DrawRect(left, top, right, bottom, _framePaint);
        }
    }
}
