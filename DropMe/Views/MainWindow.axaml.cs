using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class MainWindow : Window {

    private MainViewModel? _vm;

    public MainWindow() {
        InitializeComponent();
        TrySetWindowIcon();

        this.DataContextChanged += (_, _) => {
            if (_vm is not null) {
                _vm.SessionConnected -= OnSessionConnected;
                _vm.SessionEnded -= OnSessionEnded;
                _vm.PickFilePathUi = null;
                _vm.PickDownloadFolderUi = null;
            }

            _vm = DataContext as MainViewModel;
            if (_vm is not null) {
                _vm.SessionConnected += OnSessionConnected;
                _vm.SessionEnded += OnSessionEnded;
                _vm.PickFilePathUi = PickFilePathAsync;
                _vm.PickDownloadFolderUi = PickDownloadFolderAsync;
                _vm.SetThemeMode(IsDarkThemeActive());
            }

            var mainContent = this.FindControl<ContentControl>("MainContent")
                              ?? throw new InvalidOperationException("MainContent not found.");
            mainContent.Content = new TransferPage {
                DataContext = DataContext
            };

            UpdateThemeToggleButton();
        };
    }

    private void TrySetWindowIcon() {
        try {
            using var iconStream = AssetLoader.Open(new Uri("avares://DropMe/Assets/dropme-logo.png"));
            Icon = new WindowIcon(iconStream);
            return;
        }
        catch {
            // Fall through to fallback icon.
        }

        try {
            using var fallbackIconStream = AssetLoader.Open(new Uri("avares://DropMe/Assets/avalonia-logo.ico"));
            Icon = new WindowIcon(fallbackIconStream);
        }
        catch {
            // Keep default window icon if all custom assets fail.
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSessionConnected() {
        Dispatcher.UIThread.Post(ShowSession);
    }

    private void OnSessionEnded() {
        Dispatcher.UIThread.Post(() => {
            var mainContent = this.FindControl<ContentControl>("MainContent")
                              ?? throw new InvalidOperationException("MainContent not found.");
            mainContent.Content = new TransferPage {
                DataContext = DataContext
            };
        });
    }

    private void ThemeToggle_Click(object? sender, RoutedEventArgs e) {
        var app = Application.Current;
        if (app is null)
            return;

        var nextTheme = IsDarkThemeActive() ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = nextTheme;
        _vm?.SetThemeMode(nextTheme == ThemeVariant.Dark);
        UpdateThemeToggleButton();
    }

    private bool IsDarkThemeActive() {
        var app = Application.Current;
        if (app is null)
            return false;

        return app.ActualThemeVariant == ThemeVariant.Dark
               || app.RequestedThemeVariant == ThemeVariant.Dark;
    }

    private void UpdateThemeToggleButton() {
        var toggle = this.FindControl<Button>("ThemeToggleButton");
        if (toggle is null)
            return;

        toggle.Content = IsDarkThemeActive() ? "Light Mode" : "Dark Mode";
    }

    public void ShowSession() {
        var session = new Session {
            DataContext = DataContext
        };

        var mainContent = this.FindControl<ContentControl>("MainContent")
            ?? throw new InvalidOperationException("MainContent not found.");
        mainContent.Content = session;

        session.BackRequested += (_, _) => {
            mainContent.Content = new TransferPage {
                DataContext = DataContext
            };
        };
    }

    private async System.Threading.Tasks.Task<string?> PickFilePathAsync() {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select file to send",
            AllowMultiple = false
        });

        if (files.Count == 0)
            return null;

        var local = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(local))
            return local;

        return files[0].Path.IsFile ? files[0].Path.LocalPath : null;
    }

    private async System.Threading.Tasks.Task<string?> PickDownloadFolderAsync() {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Select download folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return null;

        var local = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(local))
            return local;

        return folders[0].Path.LocalPath;
    }
}
