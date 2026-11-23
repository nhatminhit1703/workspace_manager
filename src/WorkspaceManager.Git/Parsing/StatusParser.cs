using System.Globalization;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git.Parsing;

public enum StatusEntryKind
{
    Ordinary,
    RenamedOrCopied,
    Unmerged,
    Untracked,
    Ignored,
}

/// <summary>One path of <c>git status --porcelain=v2</c>. Status letters use '.' for "unchanged".</summary>
public sealed record StatusEntry(StatusEntryKind Kind, char IndexStatus, char WorktreeStatus, string Path, string? OriginalPath = null);

public sealed record StatusSnapshot(BranchInfo Branch, IReadOnlyList<StatusEntry> Entries)
{
    public ChangeCounts Counts
    {
        get
        {
            int staged = 0, unstaged = 0, untracked = 0, conflicted = 0, total = 0;
            foreach (var e in Entries)
            {
                switch (e.Kind)
                {
                    case StatusEntryKind.Ignored:
                        continue;
                    case StatusEntryKind.Untracked:
                        untracked++;
                        break;
                    case StatusEntryKind.Unmerged:
                        conflicted++;
                        break;
                    default:
                        if (e.IndexStatus != '.')
                        {
                            staged++;
                        }

                        if (e.WorktreeStatus != '.')
                        {
                            unstaged++;
                        }

                        break;
                }

                total++;
            }

            return new ChangeCounts(staged, unstaged, untracked, conflicted) { Total = total };
        }
    }
}

/// <summary>
/// Parser for <c>git status --porcelain=v2 --branch -z</c>.
/// Records are NUL-terminated; a rename/copy record is followed by one extra record holding the original path.
/// </summary>
public static class StatusParser
{
    public static StatusSnapshot Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string? head = null, oid = null, upstream = null;
        int? ahead = null, behind = null;
        var headSeen = false;
        var entries = new List<StatusEntry>();

        var records = output.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var r = records[i];
            if (r.Length == 0)
            {
                continue;
            }

            switch (r[0])
            {
                case '#':
                    ParseHeader(r, ref head, ref headSeen, ref oid, ref upstream, ref ahead, ref behind);
                    break;
                case '1':
                    {
                        // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
                        var f = r.Split(' ', 9);
                        entries.Add(new StatusEntry(StatusEntryKind.Ordinary, f[1][0], f[1][1], f[8]));
                        break;
                    }

                case '2':
                    {
                        // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <Xscore> <path>  NUL  <origPath>
                        var f = r.Split(' ', 10);
                        var orig = i + 1 < records.Length ? records[++i] : null;
                        entries.Add(new StatusEntry(StatusEntryKind.RenamedOrCopied, f[1][0], f[1][1], f[9], orig));
                        break;
                    }

                case 'u':
                    {
                        // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
                        var f = r.Split(' ', 11);
                        entries.Add(new StatusEntry(StatusEntryKind.Unmerged, f[1][0], f[1][1], f[10]));
                        break;
                    }

                case '?':
                    entries.Add(new StatusEntry(StatusEntryKind.Untracked, '?', '?', r[2..]));
                    break;
                case '!':
                    entries.Add(new StatusEntry(StatusEntryKind.Ignored, '!', '!', r[2..]));
                    break;
                default:
                    throw new FormatException($"Unexpected git status record: '{r}'.");
            }
        }

        var branch = headSeen ? new BranchInfo(head, oid, upstream, ahead, behind) : BranchInfo.Unknown;
        return new StatusSnapshot(branch, entries);
    }

    private static void ParseHeader(string r, ref string? head, ref bool headSeen, ref string? oid, ref string? upstream, ref int? ahead, ref int? behind)
    {
        // "# branch.<key> <value>"
        var space = r.IndexOf(' ', 2);
        if (space < 0)
        {
            return;
        }

        var key = r[2..space];
        var value = r[(space + 1)..];
        switch (key)
        {
            case "branch.oid":
                oid = value == "(initial)" ? null : value;
                break;
            case "branch.head":
                headSeen = true;
                head = value == "(detached)" ? null : value;
                break;
            case "branch.upstream":
                upstream = value;
                break;
            case "branch.ab":
                {
                    // "+<ahead> -<behind>"
                    var p = value.Split(' ');
                    ahead = int.Parse(p[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
                    behind = int.Parse(p[1].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
                    break;
                }

            default:
                break;
        }
    }
}
