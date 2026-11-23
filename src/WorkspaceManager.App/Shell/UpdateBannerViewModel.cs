using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Updates;

namespace WorkspaceManager.App.Shell;

/// <summary>Checks for a new release at startup and every few hours, shows a banner, installs on request.</summary>
public sealed partial class UpdateBannerViewModel(IUpdateService updates, ILogger<UpdateBannerViewModel> logger) : ObservableObject
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private AvailableUpdate? _update;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string? _message;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private int _progress;

    public bool IsVisible => Message is not null;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!updates.IsSupported)
        {
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                await CheckAsync(cancellationToken);
            }
            while (_update is null && await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _update = await updates.CheckAsync(cancellationToken);
            Message = _update is null ? null : $"Version {_update.Version} is available (you have {updates.CurrentVersion}).";
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Update check failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (_update is null)
        {
            return;
        }

        IsDownloading = true;
        try
        {
            await updates.DownloadAsync(_update, new Progress<int>(p => Progress = p));
            updates.ApplyAndRestart(_update);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "Update failed");
            Message = "Update failed: " + ex.Message;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private bool CanInstall() => !IsDownloading;

    [RelayCommand]
    private void Dismiss() => Message = null;
}
