using Avalonia.Interactivity;
using System;
using Avalonia.Controls;

namespace DropMe.Views;

public partial class Session : UserControl
{
    public event EventHandler? BackRequested;

    public Session()
    {
        InitializeComponent();
    }
    
    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        // Raise an event that MainWindow can handle
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}