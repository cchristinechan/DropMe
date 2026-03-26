using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DropMe.Services;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class TransferHistoryPage : UserControl {
    public event EventHandler? BackRequested;

    public TransferHistoryPage() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e) {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OpenLocation_Click(object? sender, RoutedEventArgs e) {
        if (sender is Control { DataContext: TransferHistoryEntry entry } &&
            DataContext is MainViewModel vm) {
            await vm.OpenTransferLocationAsync(entry);
        }
    }
}
