using Avalonia.Controls;

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
}