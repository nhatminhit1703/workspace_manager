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
        SyncBranch = s.SyncBranch,
        SyncStash = s.SyncStash,
        SyncKeepCurrentBranch = s.SyncKeepCurrentBranch,
        ReportDays = s.ReportDays,
    };
}

internal sealed class FakeDialogs(string? folder, bool confirm = true) : IDialogService
{
    public List<string> Questions { get; } = [];

    public Task<string?> PickFolderAsync(string title, string? startFolder = null) => Task.FromResult(folder);

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Questions.Add(message);
        return Task.FromResult(confirm);
    }

    public string? SavePath { get; set; }

    public string? Clipboard { get; private set; }

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension) => Task.FromResult(SavePath);

    public Task CopyToClipboardAsync(string text)
    {
        Clipboard = text;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSync : ISyncService
{
    public List<(SyncRequest Request, IReadOnlyList<string> Paths)> Runs { get; } = [];

    public Task<IReadOnlyList<SyncResult>> RunAsync(SyncRequest request, IReadOnlyList<string> repoPaths, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default)
    {
        Runs.Add((request, repoPaths));
        var results = repoPaths.Select(p => new SyncResult(Path.GetFileName(p), SyncOutcome.Updated, "updated", "main", "a1", "b2")).ToList();
        foreach (var r in results)
        {
            progress?.Report(r);
        }

        return Task.FromResult<IReadOnlyList<SyncResult>>(results);
    }
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
        FakePlatform? platform = null,
        ISyncService? sync = null) =>
        new(scanner, reader, sync ?? new FakeSync(), settings, dialogs ?? new FakeDialogs(null), platform ?? new FakePlatform(), NullLogger<ReposViewModel>.Instance, new FixedTime(Now));
}
