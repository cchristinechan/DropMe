using System;
using System.ComponentModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DropMe.ViewModels;
#if ANDROID
using Android.App;
using Avalonia.Android;
using Avalonia.Platform;
using DropMe.Services;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace DropMe.Views;

public partial class TransferPage : UserControl {
    private static readonly IBrush ActiveModeBrush = Brushes.White;
    private static readonly IBrush InactiveModeBrush = new SolidColorBrush(Color.Parse("#4E6479"));
    private MainViewModel? _viewModel;
    private bool? _pendingIsScanning;

    public TransferPage() {
        InitializeComponent();
        DataContextChanged += (_, _) => {
            AttachViewModel();
            InitializeNativePreviewHost();
            RefreshModeSwitchVisual();
        };
        AttachedToVisualTree += async (_, _) => {
            InitializeNativePreviewHost();
            if (DataContext is MainViewModel vm)
                await vm.PrepareMainPageAsync(homeMessage: vm.HomeSessionMessage, regenerateQr: true);
            RefreshModeSwitchVisual();
        };

        AttachedToVisualTree += async (_, _) => {
            if (DataContext is MainViewModel vm2)
                await vm2.PrepareMainPageAsync(homeMessage: vm2.HomeSessionMessage, regenerateQr: true);
        };
    }

    private void InitializeNativePreviewHost() {
        var host = this.FindControl<ContentControl>("NativePreviewHost");
        if (host is null)
            return;

        if (OperatingSystem.IsAndroid() && DataContext is MainViewModel vm && vm.UseNativeCameraPreview)
            host.Content ??= new AndroidCameraPreviewHost();
        else
            host.Content = null;
    }

    private void AttachViewModel() {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(MainViewModel.IsScanning) or nameof(MainViewModel.ShowGeneratedQr)) {
            _pendingIsScanning = null;
            RefreshModeSwitchVisual();
        }
    }

    private void RefreshModeSwitchVisual() {
        var thumb = this.FindControl<Border>("ModeSwitchThumb");
        var displayLabel = this.FindControl<TextBlock>("DisplayModeLabel");
        var scanLabel = this.FindControl<TextBlock>("ScanModeLabel");
        if (thumb?.RenderTransform is not TranslateTransform transform || displayLabel is null || scanLabel is null)
            return;

        if (transform.Transitions is null) {
            transform.Transitions = new Transitions {
                new DoubleTransition {
                    Property = TranslateTransform.XProperty,
                    Duration = TimeSpan.FromMilliseconds(180),
                    Easing = new CubicEaseOut()
                }
            };
        }

        var isScanning = _pendingIsScanning ?? (_viewModel?.IsScanning == true);
        transform.X = isScanning ? 160 : 0;
        displayLabel.Foreground = isScanning ? InactiveModeBrush : ActiveModeBrush;
        scanLabel.Foreground = isScanning ? ActiveModeBrush : InactiveModeBrush;
    }

    private async void ScanMode_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        if (!vm.IsScanning) {
            _pendingIsScanning = true;
            RefreshModeSwitchVisual();
            await System.Threading.Tasks.Task.Yield();
            await vm.StartScanAsync();
        }
    }

    private async void DisplayMode_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        _pendingIsScanning = false;
        RefreshModeSwitchVisual();
        await System.Threading.Tasks.Task.Yield();

        if (vm.IsScanning)
            await vm.StopScanAsync();

        vm.GenerateQr();
    }

    private async void FlipCamera_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.ToggleCameraAsync();
    }

    internal sealed class AndroidCameraPreviewHost : NativeControlHost {
#if ANDROID
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) {
            var context = (parent as AndroidViewControlHandle)?.View.Context ?? Application.Context;
            var cameraService = App.Services?.GetRequiredService<ICameraService>()
                ?? throw new InvalidOperationException("Camera service is not available.");

            var nativeView = cameraService.GetNativePreviewView(context);
            return new AndroidViewControlHandle(nativeView);
        }
#endif
    }


    private void DebugGoToSession_Click(object? sender, RoutedEventArgs e) {
        var parent = this.Parent;
        while (parent is not null) {
            if (parent is MainView mainView) {
                mainView.ShowSession();
                return;
            }
            parent = parent.Parent;
        }
    }
}
