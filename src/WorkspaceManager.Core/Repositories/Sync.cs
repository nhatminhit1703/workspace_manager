namespace WorkspaceManager.Core.Repositories;

public enum SyncMode
{
    /// <summary><c>git fetch origin --prune</c> only.</summary>
    FetchOnly,

    /// <summary>Fetch, switch to the target branch, fast-forward it to origin.</summary>
    CheckoutAndPull,
}

/// <summary>A bulk fetch / checkout / pull on several repositories.</summary>
/// <param name="Mode">What to do.</param>
/// <param name="Branch">"auto" = production, else master, else main; "current" = the branch each repository is on; or a branch name.</param>
/// <param name="KeepCurrentBranch">Do not switch: only fast-forward the local ref of the target branch.</param>
/// <param name="StashLocalChanges">Stash uncommitted tracked changes before switching, re-apply them after.</param>
public sealed record SyncRequest(SyncMode Mode, string Branch = SyncRequest.Auto, bool KeepCurrentBranch = false, bool StashLocalChanges = false)
{
    public const string Auto = "auto";
    public const string Current = "current";

    /// <summary>Branches tried in order by <see cref="Auto"/>.</summary>
    public static IReadOnlyList<string> AutoOrder { get; } = ["production", "master", "main"];
}

public enum SyncOutcome
{
    Fetched,
    Updated,
    UpToDate,
    RefUpdated,
    Skipped,
    Failed,
}

public sealed record SyncResult(string Repo, SyncOutcome Outcome, string Message, string? Target = null, string? Before = null, string? After = null, string? Warning = null)
{
    public bool IsOk => Outcome is SyncOutcome.Fetched or SyncOutcome.Updated or SyncOutcome.UpToDate or SyncOutcome.RefUpdated;
}

public interface ISyncService
{
    /// <summary>
    /// Runs the request on every repository (in parallel, one write at a time per repository).
    /// Never merges, rebases or resets: a branch that is not a fast-forward of origin is reported as failed.
    /// </summary>
    Task<IReadOnlyList<SyncResult>> RunAsync(SyncRequest request, IReadOnlyList<string> repoPaths, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>One writer at a time per repository, for every feature that changes a repository.</summary>
public interface IRepoLocks
{
    Task<IDisposable> AcquireAsync(string repoPath, CancellationToken cancellationToken = default);
}
