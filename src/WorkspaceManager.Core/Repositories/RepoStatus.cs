namespace WorkspaceManager.Core.Repositories;

/// <summary>Branch state of a working tree, as reported by <c>git status --branch</c>.</summary>
public sealed record BranchInfo(
    string? Head,
    string? CommitId,
    string? Upstream,
    int? Ahead,
    int? Behind)
{
    /// <summary>HEAD points to a commit, not to a branch.</summary>
    public bool IsDetached => Head is null;

    /// <summary>The branch has no commit yet (new repository).</summary>
    public bool IsUnborn => CommitId is null;

    public bool HasUpstream => Upstream is not null;

    public static BranchInfo Unknown { get; } = new(null, null, null, null, null);
}

/// <summary>Counts of the working tree entries reported by <c>git status</c> (ignored files excluded).</summary>
public sealed record ChangeCounts(int Staged, int Unstaged, int Untracked, int Conflicted)
{
    /// <summary>Number of paths with any change (a path both staged and modified counts once).</summary>
    public int Total { get; init; }

    public static ChangeCounts None { get; } = new(0, 0, 0, 0);
}

/// <summary>Snapshot of one repository of the workspace.</summary>
public sealed record RepoStatus(
    string Name,
    string Path,
    BranchInfo Branch,
    ChangeCounts Changes,
    DateTimeOffset? LastCommitDate,
    DateTimeOffset? LastFetch,
    IReadOnlySet<string> RemoteBranches,
    string? Error)
{
    public bool HasError => Error is not null;

    public bool HasRemoteBranch(string branch) => RemoteBranches.Contains(branch);
}
