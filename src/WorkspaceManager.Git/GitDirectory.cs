namespace WorkspaceManager.Git;

/// <summary>Locates the git directory of a working tree without starting git.</summary>
public static class GitDirectory
{
    /// <summary>True when <paramref name="path"/> has a <c>.git</c> folder, or a <c>.git</c> file (worktree, submodule).</summary>
    public static bool IsWorkingTree(string path) => Resolve(path) is not null;

    /// <summary>The git directory of the working tree at <paramref name="path"/>, or null.</summary>
    public static string? Resolve(string path)
    {
        var dotGit = Path.Combine(path, ".git");
        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        if (!File.Exists(dotGit))
        {
            return null;
        }

        // ".git" file: "gitdir: <path>" (relative to the working tree, or absolute)
        foreach (var line in File.ReadLines(dotGit))
        {
            const string prefix = "gitdir:";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                var target = line[prefix.Length..].Trim();
                var full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(path, target));
                return Directory.Exists(full) ? full : null;
            }
        }

        return null;
    }
}
