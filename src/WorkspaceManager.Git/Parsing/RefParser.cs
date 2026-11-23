namespace WorkspaceManager.Git.Parsing;

public static class RefParser
{
    /// <summary>
    /// Branch names of a remote from <c>git for-each-ref --format=%(refname) refs/remotes/&lt;remote&gt;</c>,
    /// without the <c>refs/remotes/&lt;remote&gt;/</c> prefix and without the symbolic <c>HEAD</c>.
    /// </summary>
    public static HashSet<string> ParseRemoteBranches(string output, string remote)
    {
        ArgumentNullException.ThrowIfNull(output);
        var prefix = $"refs/remotes/{remote}/";
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                var name = line[prefix.Length..];
                if (name.Length > 0 && name != "HEAD")
                {
                    set.Add(name);
                }
            }
        }

        return set;
    }
}
