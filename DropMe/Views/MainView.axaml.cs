using Avalonia.Controls;
using Avalonia.Interactivity;
using DropMe.ViewModels;
namespace DropMe.Views;

public partial class MainView : UserControl {
    public MainView() {
        InitializeComponent();
    }
    private void RunLocalPoc_Click(object? sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel vm)
            vm.RunLocalHelloWorldTransfer();
    }
}