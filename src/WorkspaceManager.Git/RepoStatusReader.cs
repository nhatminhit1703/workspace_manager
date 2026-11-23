using System.Globalization;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Git.Parsing;

namespace WorkspaceManager.Git;

/// <summary>Reads the status of a repository with 3 read-only git calls (status, last commit, remote branches).</summary>
public sealed class RepoStatusReader(IGitRunner git) : IRepoStatusReader
{
    public const string DefaultRemote = "origin";

    public async Task<RepoStatus> ReadAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(repoPath));
        var gitDir = GitDirectory.Resolve(repoPath);
        if (gitDir is null)
        {
            return Failed(name, repoPath, "Not a git working tree.");
        }

        var statusTask = git.RunAsync(repoPath, ["status", "--porcelain=v2", "--branch", "-z"], cancellationToken);
        var lastTask = git.RunAsync(repoPath, ["log", "-1", "--format=%cI"], cancellationToken);
        var refsTask = git.RunAsync(repoPath, ["for-each-ref", "--format=%(refname)", $"refs/remotes/{DefaultRemote}"], cancellationToken);
        await Task.WhenAll(statusTask, lastTask, refsTask).ConfigureAwait(false);

        var status = await statusTask.ConfigureAwait(false);
        if (!status.Success)
        {
            return Failed(name, repoPath, FirstLine(status.StandardError));
        }

        var snapshot = StatusParser.Parse(status.StandardOutput);

        // "git log" fails on a repository without commits: that is not an error, there is just no date.
        var last = await lastTask.ConfigureAwait(false);
        DateTimeOffset? lastCommit = last.Success && DateTimeOffset.TryParse(last.StandardOutput.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        var refs = await refsTask.ConfigureAwait(false);
        var remoteBranches = refs.Success ? RefParser.ParseRemoteBranches(refs.StandardOutput, DefaultRemote) : [];

        return new RepoStatus(name, repoPath, snapshot.Branch, snapshot.Counts, lastCommit, LastFetch(gitDir), remoteBranches, null);
    }

    private static DateTimeOffset? LastFetch(string gitDir)
    {
        var fetchHead = Path.Combine(gitDir, "FETCH_HEAD");
        return File.Exists(fetchHead) ? new DateTimeOffset(File.GetLastWriteTime(fetchHead)) : null;
    }

    private static RepoStatus Failed(string name, string path, string error) =>
        new(name, path, BranchInfo.Unknown, ChangeCounts.None, null, null, new HashSet<string>(), error);

    private static string FirstLine(string text)
    {
        var t = text.Trim();
        var nl = t.IndexOf('\n', StringComparison.Ordinal);
        return nl < 0 ? t : t[..nl].TrimEnd('\r');
    }
}

/// <summary>Finds the repositories of a workspace: the direct sub folders that are git working trees.</summary>
public sealed class WorkspaceScanner : IWorkspaceScanner
{
    public IReadOnlyList<string> FindRepositories(string workspaceRoot)
    {
        if (!Directory.Exists(workspaceRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(workspaceRoot)
            .Where(GitDirectory.IsWorkingTree)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
