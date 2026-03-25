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
        AttachedToVisualTree += (_, _) => {
            if (DataContext is MainViewModel vm)
                vm.FileOfferDecisionUi = vm.RequestFileOfferDecisionAsync;
        };
        DetachedFromVisualTree += (_, _) => {
            if (DataContext is MainViewModel vm)
                vm.FileOfferDecisionUi = null;
        };
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

    private void AcceptIncomingFile_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.AcceptPendingFileOffer();
    }

    private void RejectIncomingFile_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.RejectPendingFileOffer();
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
