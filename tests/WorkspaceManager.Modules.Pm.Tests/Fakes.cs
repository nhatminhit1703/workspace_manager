using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Platform;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Repos;

namespace WorkspaceManager.Modules.Pm.Tests;

internal sealed class FakeScanner(params string[] paths) : IWorkspaceScanner
{
    public IReadOnlyList<string> FindRepositories(string workspaceRoot) => paths.Select(p => Path.Combine(workspaceRoot, p)).ToList();
}

internal sealed class FakeReader(Func<string, RepoStatus> make) : IRepoStatusReader
{
    public int Calls { get; private set; }

    public Task<RepoStatus> ReadAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(make(repoPath));
    }
}

internal sealed class MemorySettings(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();

    public int Saves { get; private set; }

    public AppSettings Load() => Clone(Current);

    public void Save(AppSettings settings)
    {
        Current = Clone(settings);
        Saves++;
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        WorkspaceRoot = s.WorkspaceRoot,
        SelectedRepos = [.. s.SelectedRepos],
        ActiveRoleId = s.ActiveRoleId,
        LegacyImportDone = s.LegacyImportDone,
    };
}

internal sealed class FakeDialogs(string? folder) : IDialogService
{
    public Task<string?> PickFolderAsync(string title, string? startFolder = null) => Task.FromResult(folder);
}

internal sealed class FakePlatform : IPlatformService
{
    public List<string> Opened { get; } = [];

    public string AppDataDirectory => Path.GetTempPath();

    public void OpenFolder(string path) => Opened.Add("folder:" + path);

    public void OpenTerminal(string workingDirectory) => Opened.Add("terminal:" + workingDirectory);

    public void OpenInEditor(string path) => Opened.Add("editor:" + path);

    public void OpenUrl(Uri url) => Opened.Add("url:" + url);
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal static class Make
{
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    public static RepoStatus Status(string path, string branch = "main", int changes = 0, int? ahead = 0, int? behind = 0, string? error = null) =>
        new(
            Path.GetFileName(path),
            path,
            new BranchInfo(branch, "0123456789abcdef", ahead is null ? null : "origin/" + branch, ahead, behind),
            new ChangeCounts(0, changes, 0, 0) { Total = changes },
            Now.AddDays(-3),
            Now.AddHours(-2),
            new HashSet<string> { branch },
            error);

    public static ReposViewModel ViewModel(
        IWorkspaceScanner scanner,
        IRepoStatusReader reader,
        MemorySettings settings,
        IDialogService? dialogs = null,
        FakePlatform? platform = null) =>
        new(scanner, reader, settings, dialogs ?? new FakeDialogs(null), platform ?? new FakePlatform(), NullLogger<ReposViewModel>.Instance, new FixedTime(Now));
}
