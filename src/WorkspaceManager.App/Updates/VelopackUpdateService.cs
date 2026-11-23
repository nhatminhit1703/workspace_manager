using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;
using WorkspaceManager.Core.Updates;

namespace WorkspaceManager.App.Updates;

/// <summary>Updates from the GitHub releases of the public repository (no token needed).</summary>
public sealed class VelopackUpdateService : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/nhatminhit1703/workspace_manager";

    private readonly UpdateManager _manager;
    private readonly ILogger<VelopackUpdateService> _logger;

    public VelopackUpdateService(ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
    }

    public bool IsSupported => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? Shell.ShellViewModel.Version;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return null;
        }

        var info = await _manager.CheckForUpdatesAsync();
        if (info is null)
        {
            return null;
        }

        var version = info.TargetFullRelease.Version.ToString();
        _logger.LogInformation("Update {Version} is available", version);
        return new AvailableUpdate(version, info);
    }

    public Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return _manager.DownloadUpdatesAsync((UpdateInfo)update.Handle, p => progress?.Report(p), cancellationToken);
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var version = update.Version;
        _logger.LogInformation("Applying update {Version} and restarting", version);
        _manager.ApplyUpdatesAndRestart(((UpdateInfo)update.Handle).TargetFullRelease);
    }
}
