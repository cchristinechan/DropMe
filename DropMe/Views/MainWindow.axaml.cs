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

        MainContent.Content = new TransferPage();
    }

    public void ShowSession() {
        var session = new Session();
        MainContent.Content = session;

        session.BackRequested += (_, _) => {
            MainContent.Content = new TransferPage();
        };
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}
