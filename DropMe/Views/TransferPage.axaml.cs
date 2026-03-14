using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class TransferPage : UserControl {
    public TransferPage() {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) => {
            if (DataContext is MainViewModel vm)
                await vm.PrepareMainPageAsync(homeMessage: vm.HomeSessionMessage, regenerateQr: true);
        };
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
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
