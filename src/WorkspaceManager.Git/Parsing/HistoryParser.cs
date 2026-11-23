using System.Globalization;
using WorkspaceManager.Core.Reports;

namespace WorkspaceManager.Git.Parsing;

/// <summary>Parsers for the formats requested by <see cref="RepoInsightReader"/>. Fields are separated by \x1f (unit separator).</summary>
public static class HistoryParser
{
    public const char Sep = '\x1f';

    /// <summary>Format: <c>%H%x1f%h%x1f%aI%x1f%an%x1f%ae%x1f%s</c>, one commit per line.</summary>
    public const string LogFormat = "--format=%H%x1f%h%x1f%aI%x1f%an%x1f%ae%x1f%s";

    /// <summary>Format: <c>%(refname:short)%1f%(upstream:short)%1f%(upstream:track)</c> (for-each-ref uses %XX hex escapes).</summary>
    public const string BranchFormat = "--format=%(refname:short)%1f%(upstream:short)%1f%(upstream:track)";

    /// <summary>Format: <c>%gd%x1f%cI%x1f%gs</c>.</summary>
    public const string StashFormat = "--format=%gd%x1f%cI%x1f%gs";

    public static List<CommitInfo> ParseLog(string output, string repo)
    {
        ArgumentNullException.ThrowIfNull(output);
        var list = new List<CommitInfo>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Sep);
            if (p.Length < 6 || !DateTimeOffset.TryParse(p[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            list.Add(new CommitInfo(repo, p[0], p[1], date, p[3], p[4], string.Join(Sep, p[5..])));
        }

        return list;
    }

    public static List<LocalBranch> ParseBranches(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var list = new List<LocalBranch>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Sep);
            if (p.Length < 3)
            {
                continue;
            }

            // track: "", "[gone]", "[ahead 2]", "[behind 1]", "[ahead 2, behind 1]"
            var track = p[2];
            list.Add(new LocalBranch(
                p[0],
                p[1].Length > 0 ? p[1] : null,
                track.Contains("gone", StringComparison.Ordinal),
                Count(track, "ahead"),
                Count(track, "behind")));
        }

        return list;
    }

    public static List<StashEntry> ParseStashes(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var list = new List<StashEntry>();
        foreach (var line in Lines(output))
        {
            var p = line.Split(Sep);
            if (p.Length < 3)
            {
                continue;
            }

            DateTimeOffset? date = DateTimeOffset.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new StashEntry(p[0], date, p[2]));
        }

        return list;
    }

    private static int Count(string track, string word)
    {
        var i = track.IndexOf(word, StringComparison.Ordinal);
        if (i < 0)
        {
            return 0;
        }

        var digits = new string(track[(i + word.Length)..].SkipWhile(c => c == ' ').TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
}
