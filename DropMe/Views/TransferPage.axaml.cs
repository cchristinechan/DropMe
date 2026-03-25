using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DropMe.ViewModels;
using Avalonia.Media;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace DropMe.Views;

public partial class TransferPage : UserControl {
    public TransferPage() {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) => {
            if (DataContext is MainViewModel vm2)
                await vm2.PrepareMainPageAsync(homeMessage: vm2.HomeSessionMessage, regenerateQr: true);
        };
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

    private async void ScanMode_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        if (!vm.IsScanning)
            await vm.StartScanAsync();

    }

    private async void DisplayMode_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.IsScanning)
            await vm.StopScanAsync();

        vm.GenerateQr();

    }

}