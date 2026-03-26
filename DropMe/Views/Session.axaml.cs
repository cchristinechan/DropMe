using Avalonia.Interactivity;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class Session : UserControl {
    public event EventHandler? BackRequested;

    public Session() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BackButton_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm) {
            await vm.StopSessionAsync(homeMessage: "Session ended.");
            await vm.PrepareMainPageAsync(homeMessage: "Session ended.", regenerateQr: true);
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void SendFiles_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.SendFileAsync();
    }

    private async void ChooseDownloadFolder_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            await vm.ChooseDownloadFolderAsync();
    }

    // // DEBUG
    // private void DebugToggleTcp_Click(object? sender, RoutedEventArgs e) {
    //     if (DataContext is MainViewModel vm)
    //         vm.DebugToggleTcp();
    // }
    //
    // private void DebugToggleBt_Click(object? sender, RoutedEventArgs e) {
    //     if (DataContext is MainViewModel vm)
    //         vm.DebugToggleBt();
    // }
}
