using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NIInstallerTech.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnVersionClick(object? sender, RoutedEventArgs e)
    {
        await new UpdateDialog().ShowDialog(this);
    }
}