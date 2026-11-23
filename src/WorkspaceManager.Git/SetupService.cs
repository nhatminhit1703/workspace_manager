using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git;

public sealed class SetupService(IGitRunner git) : ISetupService
{
    private static string Neutral => Path.GetTempPath();

    public async Task<string?> GitVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var r = await git.RunAsync(Neutral, ["--version"], cancellationToken).ConfigureAwait(false);
            return r.Success ? r.StandardOutput.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public async Task<GitIdentity> GetIdentityAsync(CancellationToken cancellationToken = default)
    {
        var name = await git.RunAsync(Neutral, ["config", "--global", "user.name"], cancellationToken).ConfigureAwait(false);
        var email = await git.RunAsync(Neutral, ["config", "--global", "user.email"], cancellationToken).ConfigureAwait(false);
        return new GitIdentity(Value(name), Value(email));
    }

    public async Task SetIdentityAsync(GitIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsComplete)
        {
            throw new ArgumentException("Name and e-mail are both required.", nameof(identity));
        }

        foreach (var (key, value) in new[] { ("user.name", identity.Name!), ("user.email", identity.Email!) })
        {
            var r = await git.RunAsync(Neutral, ["config", "--global", key, value.Trim()], cancellationToken).ConfigureAwait(false);
            if (!r.Success)
            {
                throw new InvalidOperationException($"git config {key} failed: {r.StandardError.Trim()}");
            }
        }
    }

    public async Task<IReadOnlyList<SyncResult>> CloneMissingAsync(string workspaceRoot, IReadOnlyList<RepoEntry> entries, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Directory.CreateDirectory(workspaceRoot);
        var results = new List<SyncResult>();
        foreach (var e in entries)
        {
            SyncResult result;
            var dest = Path.Combine(workspaceRoot, e.Name);
            if (e.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || e.Name is "." or "..")
            {
                result = new SyncResult(e.Name, SyncOutcome.Failed, "invalid folder name");
            }
            else if (Directory.Exists(dest))
            {
                result = new SyncResult(e.Name, SyncOutcome.Skipped, GitDirectory.IsWorkingTree(dest) ? "already cloned" : "folder exists but is not a git repository");
            }
            else
            {
                // "--" ends the options: a URL starting with "-" cannot be read as an option.
                var r = await git.RunAsync(workspaceRoot, ["clone", "--", e.CloneUrl, e.Name], GitCommandOptions.Network with { Timeout = TimeSpan.FromMinutes(30) }, cancellationToken).ConfigureAwait(false);
                result = r.Success
                    ? new SyncResult(e.Name, SyncOutcome.Updated, "cloned")
                    : new SyncResult(e.Name, SyncOutcome.Failed, "clone failed: " + r.StandardError.Trim().Split('\n')[^1].Trim());
            }

            progress?.Report(result);
            results.Add(result);
        }

        return results;
    }

    private static string? Value(GitResult r) => r.Success && r.StandardOutput.Trim() is { Length: > 0 } v ? v : null;
}
