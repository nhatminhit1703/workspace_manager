using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git;

/// <summary>Bulk fetch / checkout / pull, same rules as the old PowerShell engine (fast-forward only).</summary>
public sealed class SyncService(IGitRunner git, IRepoLocks locks) : ISyncService
{
    private const int MaxParallel = 6;
    private const string Remote = "origin";

    public async Task<IReadOnlyList<SyncResult>> RunAsync(SyncRequest request, IReadOnlyList<string> repoPaths, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(repoPaths);
        using var gate = new SemaphoreSlim(MaxParallel);
        var tasks = repoPaths.Select(async path =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SyncResult result;
                using (await locks.AcquireAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        result = await SyncOneAsync(request, path, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException ex)
                    {
                        result = new SyncResult(Name(path), SyncOutcome.Failed, ex.Message);
                    }
                }

                progress?.Report(result);
                return result;
            }
            finally
            {
                gate.Release();
            }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<SyncResult> SyncOneAsync(SyncRequest request, string dir, CancellationToken ct)
    {
        var repo = Name(dir);
        if (!GitDirectory.IsWorkingTree(dir))
        {
            return new SyncResult(repo, SyncOutcome.Skipped, "not a git repository");
        }

        var fetch = await git.RunAsync(dir, ["fetch", Remote, "--prune"], GitCommandOptions.Network, ct).ConfigureAwait(false);
        if (!fetch.Success)
        {
            return new SyncResult(repo, SyncOutcome.Failed, "fetch failed: " + FirstLine(fetch.StandardError));
        }

        if (request.Mode == SyncMode.FetchOnly)
        {
            return new SyncResult(repo, SyncOutcome.Fetched, "fetched");
        }

        var current = (await Git(dir, ct, "branch", "--show-current").ConfigureAwait(false)).Out;
        var target = await ResolveTargetAsync(dir, request.Branch, current, ct).ConfigureAwait(false);
        if (target is null)
        {
            return new SyncResult(repo, SyncOutcome.Skipped, $"{Remote}/{request.Branch} not found");
        }

        var localExists = (await Git(dir, ct, "show-ref", "--verify", "--quiet", $"refs/heads/{target}").ConfigureAwait(false)).Ok;
        var before = localExists ? await ShortAsync(dir, target, ct).ConfigureAwait(false) : null;

        // Keep the current branch: only move the local ref of the target branch.
        if (request.KeepCurrentBranch && current != target)
        {
            if (localExists)
            {
                // "fetch origin x:x" only fast-forwards; it refuses a diverged branch.
                var r = await git.RunAsync(dir, ["fetch", Remote, $"{target}:{target}"], GitCommandOptions.Network, ct).ConfigureAwait(false);
                if (!r.Success)
                {
                    return new SyncResult(repo, SyncOutcome.Failed, $"local {target} cannot fast-forward (diverged?)", target, before);
                }
            }
            else
            {
                var b = await Git(dir, ct, "branch", "--track", target, $"{Remote}/{target}").ConfigureAwait(false);
                if (!b.Ok)
                {
                    return new SyncResult(repo, SyncOutcome.Failed, $"cannot create {target}: {b.Error}", target);
                }
            }

            var after = await ShortAsync(dir, target, ct).ConfigureAwait(false);
            return new SyncResult(repo, SyncOutcome.RefUpdated, $"ref only, still on '{current}'", target, before, after);
        }

        // Switch to the target branch and fast-forward it.
        var dirty = (await Git(dir, ct, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false)).Out;
        var stashed = false;
        if (dirty.Length > 0)
        {
            if (!request.StashLocalChanges)
            {
                return new SyncResult(repo, SyncOutcome.Skipped, $"uncommitted changes on '{current}' (use stash or keep current branch)", target, before);
            }

            var s = await Git(dir, ct, "stash", "push", "-m", $"workspace-manager auto-stash from {current}").ConfigureAwait(false);
            if (!s.Ok)
            {
                return new SyncResult(repo, SyncOutcome.Failed, "stash failed: " + s.Error, target, before);
            }

            stashed = true;
        }

        if (current != target)
        {
            var co = localExists
                ? await Git(dir, ct, "checkout", target).ConfigureAwait(false)
                : await Git(dir, ct, "checkout", "-b", target, "--track", $"{Remote}/{target}").ConfigureAwait(false);
            if (!co.Ok)
            {
                return new SyncResult(repo, SyncOutcome.Failed, $"checkout {target} failed: {co.Error}", target, before,
                    Warning: stashed ? "your changes are kept in 'git stash list'" : null);
            }
        }

        var merge = await Git(dir, ct, "merge", "--ff-only", $"{Remote}/{target}").ConfigureAwait(false);
        string? warning = null;
        SyncResult result;
        if (!merge.Ok)
        {
            result = new SyncResult(repo, SyncOutcome.Failed, $"{target} diverged from {Remote} (not a fast-forward)", target, before);
        }
        else
        {
            var after = await ShortAsync(dir, "HEAD", ct).ConfigureAwait(false);
            var updated = before != after;
            var what = before is null ? $"created from {Remote}/{target}" : updated ? "updated" : "already up to date";
            var switched = current != target ? $" (switched from '{current}')" : string.Empty;
            result = new SyncResult(repo, updated ? SyncOutcome.Updated : SyncOutcome.UpToDate, what + switched, target, before, after);
        }

        if (stashed)
        {
            var pop = await Git(dir, ct, "stash", "pop").ConfigureAwait(false);
            warning = pop.Ok ? "local changes re-applied" : "stash pop conflict: resolve manually, the changes are still in 'git stash list'";
        }

        return warning is null ? result : result with { Warning = warning };
    }

    private async Task<string?> ResolveTargetAsync(string dir, string branch, string current, CancellationToken ct)
    {
        IEnumerable<string> candidates = branch switch
        {
            SyncRequest.Current => current.Length > 0 ? [current] : [],
            SyncRequest.Auto => SyncRequest.AutoOrder,
            _ => [branch],
        };
        foreach (var c in candidates)
        {
            if ((await Git(dir, ct, "show-ref", "--verify", "--quiet", $"refs/remotes/{Remote}/{c}").ConfigureAwait(false)).Ok)
            {
                return c;
            }
        }

        return null;
    }

    private async Task<string?> ShortAsync(string dir, string rev, CancellationToken ct)
    {
        var r = await Git(dir, ct, "rev-parse", "--short", rev).ConfigureAwait(false);
        return r.Ok ? r.Out : null;
    }

    private async Task<(bool Ok, string Out, string Error)> Git(string dir, CancellationToken ct, params string[] args)
    {
        var r = await git.RunAsync(dir, args, null, ct).ConfigureAwait(false);
        return (r.Success, r.StandardOutput.Trim(), FirstLine(r.StandardError));
    }

    private static string Name(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private static string FirstLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(l => l.StartsWith("fatal:", StringComparison.Ordinal) || l.StartsWith("error:", StringComparison.Ordinal))
            ?? lines.FirstOrDefault() ?? string.Empty;
    }
}
