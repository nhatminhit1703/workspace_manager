using System.Globalization;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Git.Parsing;

namespace WorkspaceManager.Git;

/// <summary>Reads what origin/&lt;branch&gt; changes against the base branch (read-only, from the last fetch).</summary>
public sealed class BranchDiffReader(IGitRunner git) : IBranchDiffReader
{
    private static readonly string[] Bases = ["origin/production", "origin/master", "origin/main"];

    public async Task<BranchDiff?> ReadAsync(string repoPath, string branch, CancellationToken cancellationToken = default)
    {
        var repo = Path.GetFileName(Path.TrimEndingDirectorySeparator(repoPath));
        var head = $"origin/{branch}";
        if (!(await git.RunAsync(repoPath, ["show-ref", "--verify", "--quiet", $"refs/remotes/{head}"], cancellationToken).ConfigureAwait(false)).Success)
        {
            return null;
        }

        string? baseRef = null;
        foreach (var b in Bases)
        {
            if ((await git.RunAsync(repoPath, ["show-ref", "--verify", "--quiet", $"refs/remotes/{b}"], cancellationToken).ConfigureAwait(false)).Success)
            {
                baseRef = b;
                break;
            }
        }

        if (baseRef is null || baseRef == head)
        {
            return null;
        }

        var mb = (await git.RunAsync(repoPath, ["merge-base", baseRef, head], cancellationToken).ConfigureAwait(false)).StandardOutput.Trim();
        if (mb.Length == 0)
        {
            return null;
        }

        var namesTask = git.RunAsync(repoPath, ["diff", "--name-status", "--no-renames", "-z", mb, head], cancellationToken);
        var treeTask = git.RunAsync(repoPath, ["ls-tree", "-r", "--name-only", "-z", head], cancellationToken);
        var diffTask = git.RunAsync(repoPath, ["diff", "-U0", "--no-color", "--no-renames", "--no-ext-diff", mb, head], cancellationToken);
        var logTask = git.RunAsync(repoPath, ["log", "--no-merges", HistoryParser.LogFormat, $"{mb}..{head}"], cancellationToken);
        await Task.WhenAll(namesTask, treeTask, diffTask, logTask).ConfigureAwait(false);

        var changed = ParseNameStatus((await namesTask.ConfigureAwait(false)).StandardOutput);
        var tree = (await treeTask.ConfigureAwait(false)).StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var added = ParseAddedLines((await diffTask.ConfigureAwait(false)).StandardOutput);
        var commits = HistoryParser.ParseLog((await logTask.ConfigureAwait(false)).StandardOutput, repo);
        return new BranchDiff(repo, branch, baseRef, changed, tree, added, commits);
    }

    /// <summary><c>diff --name-status -z</c>: "M\0path\0A\0path\0...".</summary>
    public static Dictionary<string, char> ParseNameStatus(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var parts = output.Split('\0');
        var map = new Dictionary<string, char>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            if (parts[i].Length > 0)
            {
                map[parts[i + 1]] = parts[i][0];
            }
        }

        return map;
    }

    /// <summary>Added lines of a unified diff with zero context (<c>-U0</c>), with their new line numbers.</summary>
    public static List<AddedLine> ParseAddedLines(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var list = new List<AddedLine>();
        string? file = null;
        var line = 0;
        foreach (var raw in diff.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("diff --git", StringComparison.Ordinal))
            {
                file = null;
            }
            else if (l.StartsWith("+++ ", StringComparison.Ordinal))
            {
                file = l == "+++ /dev/null" ? null : l.StartsWith("+++ b/", StringComparison.Ordinal) ? l[6..] : l[4..];
            }
            else if (l.StartsWith("--- ", StringComparison.Ordinal))
            {
                continue;
            }
            else if (l.StartsWith("@@", StringComparison.Ordinal))
            {
                // @@ -a,b +c,d @@
                var plus = l.IndexOf('+', StringComparison.Ordinal);
                var end = l.IndexOfAny([',', ' '], plus + 1);
                line = int.TryParse(l.AsSpan(plus + 1, end - plus - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            }
            else if (file is not null && l.StartsWith('+'))
            {
                list.Add(new AddedLine(file, line, l[1..]));
                line++;
            }
        }

        return list;
    }
}
