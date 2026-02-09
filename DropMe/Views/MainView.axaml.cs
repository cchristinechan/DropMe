using Avalonia.Controls;
using Avalonia.Interactivity;
using DropMe.ViewModels;
using Avalonia.VisualTree;
namespace DropMe.Views;


public partial class TransferPage : UserControl {
    
    public TransferPage() {
        InitializeComponent();
    }
    
    private void RunLocalPoc_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.RunLocalHelloWorldTransfer();
    }
    
    private void SessionStart_Click(object? sender, RoutedEventArgs e)
    {
        // Get the hosting window
        if (this.GetVisualRoot() is MainWindow mainWindow)
        {
            mainWindow.ShowSession();
        }
    }
}