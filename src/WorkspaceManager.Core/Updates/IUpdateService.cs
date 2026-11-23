namespace WorkspaceManager.Core.Updates;

/// <summary>A newer release found by <see cref="IUpdateService.CheckAsync"/>. <see cref="Handle"/> belongs to the implementation.</summary>
public sealed record AvailableUpdate(string Version, object Handle);

/// <summary>Self-update from the published releases.</summary>
public interface IUpdateService
{
    /// <summary>False when the app runs from a development build (not installed): nothing to update.</summary>
    bool IsSupported { get; }

    string CurrentVersion { get; }

    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Installs the downloaded update and restarts the application.</summary>
    void ApplyAndRestart(AvailableUpdate update);
}
