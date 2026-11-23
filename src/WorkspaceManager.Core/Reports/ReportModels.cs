using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Core.Reports;

/// <summary>Everything the PM report needs about one repository, read-only.</summary>
public sealed record RepoInsight(
    RepoStatus Status,
    DateTimeOffset? OldestChangedFile,
    IReadOnlyList<StashEntry> Stashes,
    IReadOnlyList<LocalBranch> LocalBranches,
    IReadOnlyList<CommitInfo> Commits,
    string? UserEmail,
    BranchTracking? Tracking);

public sealed record StashEntry(string Name, DateTimeOffset? Date, string Message);

/// <summary>A local branch and its upstream state (<c>for-each-ref %(upstream:track)</c>).</summary>
public sealed record LocalBranch(string Name, string? Upstream, bool UpstreamGone, int Ahead, int Behind);

public sealed record CommitInfo(string Repo, string Hash, string ShortHash, DateTimeOffset Date, string Author, string Email, string Subject);

/// <summary>State of the focus branch (the one typed in the branch box) in one repository.</summary>
public sealed record BranchTracking(string Branch, bool OnRemote, bool OnLocal, bool IsCurrent, int? Ahead, int? Behind);

public enum ActionLevel
{
    High,
    Medium,
    Low,
    Info,
}

public sealed record ActionItem(ActionLevel Level, string Repo, string Issue, string SuggestedAction);

public sealed record ReportKpi(string Name, int Value, KpiTone Tone);

public enum KpiTone
{
    Neutral,
    Warning,
    Bad,
}

/// <summary>Options of one report run.</summary>
/// <param name="Days">Activity window, in days, ending today.</param>
/// <param name="FocusBranch">Branch to track across repositories, or null.</param>
/// <param name="StaleFetchDays">A fetch older than this is reported.</param>
/// <param name="OldChangesDays">Uncommitted files older than this are a high priority item.</param>
public sealed record ReportOptions(int Days = 7, string? FocusBranch = null, int StaleFetchDays = 3, int OldChangesDays = 7);

public sealed record WorkspaceReport(
    DateTimeOffset GeneratedAt,
    string WorkspaceRoot,
    ReportOptions Options,
    IReadOnlyList<ReportKpi> Kpis,
    IReadOnlyList<ActionItem> Actions,
    IReadOnlyList<RepoInsight> Repos,
    IReadOnlyList<CommitInfo> Commits,
    IReadOnlyList<CommitInfo> MyCommits);

public interface IRepoInsightReader
{
    Task<RepoInsight> ReadAsync(string repoPath, ReportOptions options, DateTimeOffset now, CancellationToken cancellationToken = default);
}
