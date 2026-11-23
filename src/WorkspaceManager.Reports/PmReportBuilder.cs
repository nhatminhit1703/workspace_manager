using System.Globalization;
using WorkspaceManager.Core.Reports;

namespace WorkspaceManager.Reports;

/// <summary>
/// Turns repository insights into KPIs and prioritized action items.
/// Same rules as the PowerShell PM report, so the numbers can be compared.
/// </summary>
public static class PmReportBuilder
{
    public static WorkspaceReport Build(string workspaceRoot, IReadOnlyList<RepoInsight> repos, ReportOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(repos);
        ArgumentNullException.ThrowIfNull(options);
        var actions = new List<ActionItem>();
        var attention = 0;
        foreach (var r in repos)
        {
            var before = actions.Count;
            AddRules(r, options, now, actions);
            if (actions.Skip(before).Any(a => a.Level is ActionLevel.High or ActionLevel.Medium))
            {
                attention++;
            }
        }

        actions = [.. actions.OrderBy(a => a.Level).ThenBy(a => a.Repo, StringComparer.OrdinalIgnoreCase)];

        // The same commit can be reachable from several clones: keep it once.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var commits = repos.SelectMany(r => r.Commits).Where(c => seen.Add(c.Hash)).OrderByDescending(c => c.Date).ToList();
        var myEmails = repos.Select(r => r.UserEmail).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = commits.Where(c => myEmails.Contains(c.Email)).ToList();

        var kpis = new List<ReportKpi>
        {
            new("Repos", repos.Count, KpiTone.Neutral),
            Kpi("Need attention", attention, KpiTone.Warning),
            Kpi("High priority items", actions.Count(a => a.Level == ActionLevel.High), KpiTone.Bad),
            Kpi("Uncommitted repos", repos.Count(r => r.Status.Changes.Total > 0), KpiTone.Warning),
            Kpi("Unpushed repos", repos.Count(r => r.Status.Branch.Ahead > 0 || UnpushedOtherBranches(r).Any()), KpiTone.Bad),
            Kpi("Behind origin", repos.Count(r => r.Status.Branch.Behind > 0), KpiTone.Warning),
            Kpi("Stashes", repos.Sum(r => r.Stashes.Count), KpiTone.Warning),
            new(string.Create(CultureInfo.InvariantCulture, $"Commits ({options.Days} d)"), commits.Count, KpiTone.Neutral),
            new(string.Create(CultureInfo.InvariantCulture, $"My commits ({options.Days} d)"), mine.Count, KpiTone.Neutral),
        };

        return new WorkspaceReport(now, workspaceRoot, options, kpis, actions, repos, commits, mine);
    }

    private static ReportKpi Kpi(string name, int value, KpiTone toneWhenNotZero) =>
        new(name, value, value > 0 ? toneWhenNotZero : KpiTone.Neutral);

    private static IEnumerable<LocalBranch> UnpushedOtherBranches(RepoInsight r) =>
        r.LocalBranches.Where(b => !b.UpstreamGone && b.Upstream is not null && b.Ahead > 0 && b.Name != r.Status.Branch.Head);

    private static void AddRules(RepoInsight r, ReportOptions o, DateTimeOffset now, List<ActionItem> actions)
    {
        var s = r.Status;
        var name = s.Name;
        void Add(ActionLevel level, string issue, string fix) => actions.Add(new ActionItem(level, name, issue, fix));

        if (s.HasError)
        {
            Add(ActionLevel.High, "Cannot read the repository: " + s.Error, "Open the folder and check it with git");
            return;
        }

        var b = s.Branch;
        var cur = b.Head ?? "HEAD";
        int ahead = b.Ahead ?? 0, behind = b.Behind ?? 0;

        if (b.IsDetached)
        {
            Add(ActionLevel.High, $"Detached HEAD at {Short(b.CommitId)}", "Checkout a branch; create one if you have work here");
        }

        if (ahead > 0 && behind > 0)
        {
            Add(ActionLevel.High, $"Branch '{cur}' diverged from {b.Upstream} (ahead {ahead}, behind {behind})", "Rebase or merge, then push");
        }
        else if (ahead > 0)
        {
            Add(ActionLevel.High, $"{ahead} commit(s) on '{cur}' not pushed", "Push");
        }

        var others = UnpushedOtherBranches(r).Select(x => $"{x.Name} (+{x.Ahead})").ToList();
        if (others.Count > 0)
        {
            Add(ActionLevel.High, "Other branches with unpushed commits: " + string.Join(", ", others), "Push or delete them");
        }

        var files = s.Changes.Total;
        if (files > 0)
        {
            var age = r.OldestChangedFile is { } t ? ", oldest file modified " + Age(now - t) : string.Empty;
            if (r.OldestChangedFile is { } old && (now - old).TotalDays >= o.OldChangesDays)
            {
                Add(ActionLevel.High, $"{files} uncommitted file(s){age}", "Commit, stash or discard");
            }
            else
            {
                Add(ActionLevel.Medium, $"{files} uncommitted file(s){age}", "Commit when the work is done");
            }
        }

        if (behind > 0 && ahead == 0)
        {
            Add(ActionLevel.Medium, $"'{cur}' is {behind} commit(s) behind {b.Upstream}", "Pull (Repositories > Pull current branch)");
        }

        var currentGone = r.LocalBranches.Any(x => x.Name == b.Head && x.UpstreamGone);
        if (currentGone)
        {
            Add(ActionLevel.Medium, $"Current branch '{cur}' was deleted on origin (merged or closed?)", "Switch to the main branch, then delete this one");
        }
        else if (!b.IsDetached && !b.IsUnborn && !b.HasUpstream)
        {
            Add(ActionLevel.Medium, $"Current branch '{cur}' has no upstream (not pushed)", $"git push -u origin {cur}");
        }

        if (r.Stashes.Count > 0)
        {
            Add(ActionLevel.Medium, $"{r.Stashes.Count} stash(es) left over", "Pop or drop them");
        }

        var gone = r.LocalBranches.Where(x => x.UpstreamGone && x.Name != b.Head).Select(x => x.Name).ToList();
        if (gone.Count > 0)
        {
            var names = string.Join(", ", gone.Take(3)) + (gone.Count > 3 ? $", ... (+{gone.Count - 3})" : string.Empty);
            Add(ActionLevel.Low, $"{gone.Count} local branch(es) whose remote was deleted: {names}", "Delete them (git branch -d)");
        }

        if (s.LastFetch is null)
        {
            Add(ActionLevel.Low, "Never fetched", "Fetch");
        }
        else if ((now - s.LastFetch.Value).TotalDays >= o.StaleFetchDays)
        {
            Add(ActionLevel.Low, "Last fetch " + Age(now - s.LastFetch.Value), "Fetch");
        }

        if (r.Tracking is { } t2)
        {
            if (t2.OnRemote && !t2.IsCurrent)
            {
                Add(ActionLevel.Info, $"origin/{t2.Branch} exists but is not checked out (now on '{cur}')", "Repositories > Checkout + Pull");
            }

            if (t2.IsCurrent && t2.Behind > 0)
            {
                Add(ActionLevel.Medium, $"'{t2.Branch}' is behind origin by {t2.Behind}", "Pull");
            }
        }
    }

    public static string Age(TimeSpan span)
    {
        if (span.TotalMinutes < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes} min ago");
        }

        if (span.TotalHours < 24)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h ago");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays} days ago");
    }

    private static string Short(string? id) => id is { Length: > 7 } ? id[..7] : id ?? "?";
}
