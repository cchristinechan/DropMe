using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DropMe.ViewModels;

namespace DropMe.Views;

public partial class MainView : UserControl {
    private enum MainPageKind {
        Transfer,
        Session,
        History
    }

    private MainViewModel? _vm;
    private MainPageKind _currentPage = MainPageKind.Transfer;
    private MainPageKind _previousPageBeforeHistory = MainPageKind.Transfer;

    public MainView() {
        InitializeComponent();

        this.DataContextChanged += (_, _) => {
            if (_vm is not null) {
                _vm.SessionConnected -= OnSessionConnected;
                _vm.SessionEnded -= OnSessionEnded;
                _vm.PickFileStreamUi = null;
                _vm.PickDownloadFolderUi = null;
                _vm.FileOfferDecisionUi = null;
            }

            _vm = DataContext as MainViewModel;
            if (_vm is not null) {
                _vm.SessionConnected += OnSessionConnected;
                _vm.SessionEnded += OnSessionEnded;
                _vm.PickFileStreamUi = PickFileStreamAsync;
                _vm.PickDownloadFolderUi = PickDownloadFolderAsync;
                _vm.FileOfferDecisionUi = _vm.RequestFileOfferDecisionAsync;
            }

            ShowTransferPage();
        };
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }


    private void OnSessionConnected() {
        Dispatcher.UIThread.Post(ShowSession);
    }

    private void OnSessionEnded() {
        Dispatcher.UIThread.Post(ShowTransferPage);
    }

    public void ShowSession() {
        var session = CreateSessionPage();
        SetMainContent(session, MainPageKind.Session);
    }

    private void ShowTransferPage() {
        SetMainContent(new TransferPage {
            DataContext = DataContext
        }, MainPageKind.Transfer);
    }

    private void ShowHistoryPage() {
        if (_currentPage != MainPageKind.History)
            _previousPageBeforeHistory = _currentPage;

        var historyPage = new TransferHistoryPage {
            DataContext = DataContext
        };
        historyPage.BackRequested += (_, _) => {
            if (_previousPageBeforeHistory == MainPageKind.Session) {
                ShowSession();
            }
            else {
                ShowTransferPage();
            }
        };

        SetMainContent(historyPage, MainPageKind.History);
    }

    private Session CreateSessionPage() {
        var session = new Session {
            DataContext = DataContext
        };
        session.BackRequested += (_, _) => {
            ShowTransferPage();
        };
        return session;
    }

    private void SetMainContent(Control page, MainPageKind pageKind) {
        var mainContent = this.FindControl<ContentControl>("MainContent")
                          ?? throw new InvalidOperationException("MainContent not found.");
        mainContent.Content = page;
        _currentPage = pageKind;
        UpdateShellChrome();
    }

    private void UpdateShellChrome() {
        if (this.FindControl<Button>("HistoryButton") is { } historyButton)
            historyButton.IsVisible = _currentPage is not MainPageKind.History;
    }

    private void HistoryButton_Click(object? sender, RoutedEventArgs e) {
        ShowHistoryPage();
    }

    private void AcceptIncomingFile_Click(object? sender, RoutedEventArgs e) {
        _vm?.AcceptPendingFileOffer();
    }

    private void RejectIncomingFile_Click(object? sender, RoutedEventArgs e) {
        _vm?.RejectPendingFileOffer();
    }

    private async System.Threading.Tasks.Task<(string, Stream)?> PickFileStreamAsync() {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
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
        if (_vm is not null)
            await _vm.DoPickDownloadsFolder(this);
    }
}
