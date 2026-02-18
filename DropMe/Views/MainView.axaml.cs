using Avalonia.Interactivity;
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DropMe.ViewModels;
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class MainView : UserControl {
    private MainViewModel? _vm;

    public MainView() {
        InitializeComponent();

        this.DataContextChanged += (_, _) => {
            if (_vm is not null) {
                _vm.SessionConnected -= OnSessionConnected;
                _vm.SessionEnded -= OnSessionEnded;
                _vm.PickFileStreamUi = null;
                _vm.PickDownloadFolderUi = null;
            }

            _vm = DataContext as MainViewModel;
            if (_vm is not null) {
                _vm.SessionConnected += OnSessionConnected;
                _vm.SessionEnded += OnSessionEnded;
                _vm.PickFileStreamUi = PickFileStreamAsync;
                _vm.PickDownloadFolderUi = PickDownloadFolderAsync;
            }

            var mainContent = this.FindControl<ContentControl>("MainContent")
                              ?? throw new InvalidOperationException("MainContent not found.");
            mainContent.Content = new TransferPage {
                DataContext = DataContext
            };
        };
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

    private async System.Threading.Tasks.Task<(string, Stream)?> PickFileStreamAsync() {
        var files = await TopLevel.GetTopLevel(this)?.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Select file to send",
            AllowMultiple = false
        });

        if (files.Count > 0) {
            var filename = files[0].Name;
            var stream = await files[0].OpenReadAsync();

            return (filename, stream);
        }
        return null;
    }

    private async System.Threading.Tasks.Task PickDownloadFolderAsync() {
        await _vm.DoPickDownloadsFolder(this);
    }
}
