using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DropMe.ViewModels;
namespace DropMe.Views;
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
    }
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}