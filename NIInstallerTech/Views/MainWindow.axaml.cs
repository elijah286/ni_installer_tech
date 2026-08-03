using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NIInstallerTech.Services;

namespace NIInstallerTech.Views;

public partial class MainWindow : Window
{
    private readonly GitHubReleaseUpdateService _updateService = new();
    private UpdateRelease? _availableUpdate;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            _availableUpdate = await _updateService.CheckForUpdateAsync();
            if (_availableUpdate is not null)
                UpdateBadge.IsVisible = true;
        }
        catch
        {
            // silent — update check is best-effort on startup
        }
    }

    private async void OnVersionClick(object? sender, RoutedEventArgs e)
    {
        await new UpdateDialog(_availableUpdate).ShowDialog(this);
        UpdateBadge.IsVisible = false;
        _availableUpdate = null;
    }
}