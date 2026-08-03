using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NIInstallerTech.Services;

namespace NIInstallerTech.Views;

public partial class UpdateDialog : Window
{
    private readonly GitHubReleaseUpdateService _updateService = new();
    private UpdateRelease? _availableUpdate;

    public UpdateDialog()
    {
        InitializeComponent();
        CurrentVersionText.Text = $"Installed version {AppVersion.Display}";
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        ReleaseNotesText.IsVisible = false;
        StatusText.Text = "Checking the approved update source...";

        try
        {
            _availableUpdate = await _updateService.CheckForUpdateAsync();
            if (_availableUpdate is null)
            {
                StatusText.Text = "You're running the latest available version.";
                return;
            }

            StatusText.Text = $"Version {_availableUpdate.Version} is ready to install.";
            ReleaseNotesText.Text = _availableUpdate.Notes;
            ReleaseNotesText.IsVisible = !string.IsNullOrWhiteSpace(_availableUpdate.Notes);
            InstallButton.IsEnabled = true;
        }
        catch (UpdateFeedUnavailableException exception)
        {
            StatusText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Unable to check for updates: {exception.Message}";
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
        {
            return;
        }

        CheckButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        DownloadProgress.IsVisible = true;
        DownloadProgress.Value = 0;
        StatusText.Text = $"Downloading version {_availableUpdate.Version}...";

        try
        {
            var progress = new Progress<double>(value => DownloadProgress.Value = value);
            var archivePath = await _updateService.DownloadAndVerifyAsync(_availableUpdate, progress);
            StatusText.Text = "Verified update. Restarting NI Setup...";
            WindowsUpdateInstaller.Schedule(archivePath);
            Dispatcher.UIThread.Post(Close);
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Unable to install the update: {exception.Message}";
            InstallButton.IsEnabled = true;
            CheckButton.IsEnabled = true;
        }
        finally
        {
            DownloadProgress.IsVisible = false;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}