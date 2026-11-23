namespace WorkspaceManager.Core.Repositories;

/// <summary>A repository the workspace should contain (kept in the local settings, never in this repository).</summary>
public sealed record RepoEntry(string Name, string CloneUrl)
{
    /// <summary>Parses "name | url" or a bare URL per line; blank lines and "#" comments are skipped.</summary>
    public static List<RepoEntry> ParseList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<RepoEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            var url = parts.Length > 1 ? parts[1] : parts[0];
            var name = parts.Length > 1 && parts[0].Length > 0 ? parts[0] : NameFromUrl(url);
            if (url.Length > 0 && name.Length > 0)
            {
                list.Add(new RepoEntry(name, url));
            }
        }

        return list;
    }

    public static string NameFromUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var last = url.TrimEnd('/', '\\').Split('/', '\\', ':').LastOrDefault() ?? string.Empty;
        return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    public static string Format(IEnumerable<RepoEntry> entries) => string.Join('\n', entries.Select(e => $"{e.Name} | {e.CloneUrl}"));
}

public sealed record GitIdentity(string? Name, string? Email)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Email);
}

/// <summary>Machine checks and one-time setup actions.</summary>
public interface ISetupService
{
    /// <summary>"git version x.y.z", or null when git cannot be started.</summary>
    Task<string?> GitVersionAsync(CancellationToken cancellationToken = default);

    Task<GitIdentity> GetIdentityAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes user.name / user.email to the global git configuration (only on the user's request).</summary>
    Task SetIdentityAsync(GitIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Clones the entries whose folder does not exist yet under <paramref name="workspaceRoot"/>.</summary>
    Task<IReadOnlyList<SyncResult>> CloneMissingAsync(string workspaceRoot, IReadOnlyList<RepoEntry> entries, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Opens a page of the shell by navigation id (used by dashboard shortcuts).</summary>
public interface INavigator
{
    void Navigate(string navigationId);
}
