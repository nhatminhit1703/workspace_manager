using System.Globalization;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Git.Parsing;

namespace WorkspaceManager.Git;

/// <summary>Collects the data of the PM report for one repository. Read-only: never fetches or changes anything.</summary>
public sealed class RepoInsightReader(IGitRunner git, IRepoStatusReader statusReader) : IRepoInsightReader
{
    private const int MaxCommits = 300;
    private const int MaxFilesForAge = 300;

    public async Task<RepoInsight> ReadAsync(string repoPath, ReportOptions options, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var status = await statusReader.ReadAsync(repoPath, cancellationToken).ConfigureAwait(false);
        if (status.HasError)
        {
            return new RepoInsight(status, null, [], [], [], null, null);
        }

        var since = now.Date.AddDays(-(options.Days - 1)).ToString("yyyy-MM-dd 00:00", CultureInfo.InvariantCulture);
        var stashTask = git.RunAsync(repoPath, ["stash", "list", HistoryParser.StashFormat], cancellationToken);
        var branchTask = git.RunAsync(repoPath, ["for-each-ref", HistoryParser.BranchFormat, "refs/heads"], cancellationToken);
        // --branches --remotes --tags, not --all: --all includes stash commits.
        var logTask = git.RunAsync(repoPath, ["log", "--branches", "--remotes", "--tags", "--no-merges", $"--since={since}", HistoryParser.LogFormat, "-n", MaxCommits.ToString(CultureInfo.InvariantCulture)], cancellationToken);
        var emailTask = git.RunAsync(repoPath, ["config", "user.email"], cancellationToken);
        var porcelainTask = git.RunAsync(repoPath, ["status", "--porcelain=v2", "-z"], cancellationToken);
        await Task.WhenAll(stashTask, branchTask, logTask, emailTask, porcelainTask).ConfigureAwait(false);

        var name = status.Name;
        var stashes = HistoryParser.ParseStashes((await stashTask.ConfigureAwait(false)).StandardOutput);
        var branches = HistoryParser.ParseBranches((await branchTask.ConfigureAwait(false)).StandardOutput);
        var commits = HistoryParser.ParseLog((await logTask.ConfigureAwait(false)).StandardOutput, name);
        var email = (await emailTask.ConfigureAwait(false)).StandardOutput.Trim();
        var oldest = OldestChangedFile(repoPath, (await porcelainTask.ConfigureAwait(false)).StandardOutput);

        BranchTracking? tracking = null;
        if (!string.IsNullOrWhiteSpace(options.FocusBranch))
        {
            tracking = await TrackAsync(repoPath, options.FocusBranch.Trim(), status, branches, cancellationToken).ConfigureAwait(false);
        }

        return new RepoInsight(status, oldest, stashes, branches, commits, email.Length > 0 ? email : null, tracking);
    }

    private async Task<BranchTracking> TrackAsync(string dir, string branch, RepoStatus status, List<LocalBranch> locals, CancellationToken ct)
    {
        var onRemote = status.HasRemoteBranch(branch);
        var onLocal = locals.Any(b => b.Name == branch);
        int? ahead = null, behind = null;
        if (onRemote && onLocal)
        {
            var r = await git.RunAsync(dir, ["rev-list", "--left-right", "--count", $"origin/{branch}...{branch}"], ct).ConfigureAwait(false);
            var p = r.StandardOutput.Split((char[])['\t', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (r.Success && p.Length == 2)
            {
                behind = int.Parse(p[0], CultureInfo.InvariantCulture);
                ahead = int.Parse(p[1], CultureInfo.InvariantCulture);
            }
        }

        return new BranchTracking(branch, onRemote, onLocal, status.Branch.Head == branch, ahead, behind);
    }

    /// <summary>Modification time of the oldest changed file: how long uncommitted work has been lying around.</summary>
    private static DateTimeOffset? OldestChangedFile(string repoPath, string porcelain)
    {
        DateTimeOffset? oldest = null;
        foreach (var e in StatusParser.Parse(porcelain).Entries.Where(e => e.Kind != StatusEntryKind.Ignored).Take(MaxFilesForAge))
        {
            // An untracked folder is listed as "dir/": use the folder's own time, like the old tool.
            var full = Path.Combine(repoPath, e.Path.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            DateTime time;
            if (File.Exists(full))
            {
                time = File.GetLastWriteTime(full);
            }
            else if (Directory.Exists(full))
            {
                time = Directory.GetLastWriteTime(full);
            }
            else
            {
                continue;
            }

            var t = new DateTimeOffset(time);
            if (oldest is null || t < oldest)
            {
                oldest = t;
            }
        }

        return oldest;
    }
}
