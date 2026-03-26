using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DropMe.Controls;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class TransferPage : UserControl {
    public TransferPage() {
        InitializeComponent();
        DataContextChanged += (_, _) => InitializeNativePreviewHost();
        AttachedToVisualTree += async (_, _) => {
            InitializeNativePreviewHost();
            if (DataContext is MainViewModel vm)
                await vm.PrepareMainPageAsync(homeMessage: vm.HomeSessionMessage, regenerateQr: true);
        };
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeNativePreviewHost() {
        var host = this.FindControl<ContentControl>("NativePreviewHost");
        if (host is null)
            return;

        if (OperatingSystem.IsAndroid() && DataContext is MainViewModel vm && vm.UseNativeCameraPreview) {
            host.Content ??= new AndroidCameraPreviewHost();
        }
        else {
            host.Content = null;
        }
    }

    private void GenerateQr_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.GenerateQr();
    }

    private async void ToggleScan_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.IsScanning) {
            await vm.StopScanAsync();
            vm.GenerateQr();
        }
        else {
            await vm.StartScanAsync();
        }
    }

    private async void FlipCamera_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.ToggleCameraAsync();
    }
}
