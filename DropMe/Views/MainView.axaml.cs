using Avalonia.Controls;
using Avalonia.Interactivity;
using DropMe.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Views;

public partial class MainView : UserControl {
    public MainView() {
        InitializeComponent();

        if (App.Services is not null)
            DataContext = App.Services.GetRequiredService<MainViewModel>();
    }

    private void GenerateQr_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.GenerateQr();
    }

    private async void ScanQr_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.StartScanAsync();
    }

    private async void StopScan_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.StopScanAsync();
    }
    private async void SendFile_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.SendFileAsync();
    }

}