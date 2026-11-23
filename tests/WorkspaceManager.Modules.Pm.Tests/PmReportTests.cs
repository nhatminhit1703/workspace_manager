using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Report;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Tests;

internal static class Insight
{
    public static RepoInsight Of(
        string name,
        string? branch = "main",
        int changes = 0,
        int? ahead = 0,
        int? behind = 0,
        DateTimeOffset? oldest = null,
        int stashes = 0,
        LocalBranch[]? branches = null,
        CommitInfo[]? commits = null,
        DateTimeOffset? lastFetch = null,
        BranchTracking? tracking = null,
        string? error = null,
        string email = "me@example.com")
    {
        var status = new RepoStatus(
            name,
            "/ws/" + name,
            new BranchInfo(branch, "0123456789abcdef", ahead is null ? null : "origin/" + branch, ahead, behind),
            new ChangeCounts(0, changes, 0, 0) { Total = changes },
            Make.Now.AddDays(-1),
            lastFetch ?? Make.Now.AddHours(-1),
            new HashSet<string> { "main" },
            error);
        return new RepoInsight(
            status,
            oldest,
            Enumerable.Range(0, stashes).Select(i => new StashEntry($"stash@{{{i}}}", Make.Now, "wip")).ToList(),
            branches ?? [new LocalBranch(branch ?? "x", "origin/" + branch, false, ahead ?? 0, behind ?? 0)],
            commits ?? [],
            email,
            tracking);
    }

    public static CommitInfo Commit(string repo, string hash, string author, string email, int hoursAgo, string subject = "work") =>
        new(repo, hash, hash[..Math.Min(7, hash.Length)], Make.Now.AddHours(-hoursAgo), author, email, subject);
}

public class PmReportBuilderTests
{
    private static List<ActionItem> Actions(RepoInsight r, ReportOptions? o = null) =>
        PmReportBuilder.Build("/ws", [r], o ?? new ReportOptions(), Make.Now).Actions.ToList();

    [Fact]
    public void Clean_repository_has_no_action()
    {
        Actions(Insight.Of("api")).Should().BeEmpty();
    }

    [Fact]
    public void Unpushed_commits_are_high()
    {
        Actions(Insight.Of("api", ahead: 2)).Should().ContainSingle(a => a.Level == ActionLevel.High && a.Issue == "2 commit(s) on 'main' not pushed");
    }

    [Fact]
    public void Diverged_branch_is_high()
    {
        Actions(Insight.Of("api", ahead: 1, behind: 3)).Single().Issue.Should().Contain("diverged");
    }

    [Fact]
    public void Old_uncommitted_files_are_high_recent_ones_medium()
    {
        Actions(Insight.Of("api", changes: 3, oldest: Make.Now.AddDays(-10))).Single().Level.Should().Be(ActionLevel.High);
        Actions(Insight.Of("api", changes: 3, oldest: Make.Now.AddDays(-1))).Single().Level.Should().Be(ActionLevel.Medium);
    }

    [Fact]
    public void Behind_upstream_is_medium()
    {
        Actions(Insight.Of("api", behind: 4)).Single().Should().Be(new ActionItem(ActionLevel.Medium, "api", "'main' is 4 commit(s) behind origin/main", "Pull (Repositories > Pull current branch)"));
    }

    [Fact]
    public void No_upstream_and_gone_upstream()
    {
        Actions(Insight.Of("api", ahead: null, behind: null, branches: [new LocalBranch("main", null, false, 0, 0)]))
            .Single().Issue.Should().Be("Current branch 'main' has no upstream (not pushed)");

        Actions(Insight.Of("api", ahead: null, behind: null, branches: [new LocalBranch("main", "origin/main", true, 0, 0)]))
            .Single().Issue.Should().Contain("was deleted on origin");
    }

    [Fact]
    public void Stashes_gone_branches_and_stale_fetch()
    {
        var r = Insight.Of("api", stashes: 2, lastFetch: Make.Now.AddDays(-5), branches:
        [
            new LocalBranch("main", "origin/main", false, 0, 0),
            new LocalBranch("old-1", "origin/old-1", true, 0, 0),
            new LocalBranch("old-2", "origin/old-2", true, 0, 0),
        ]);

        var a = Actions(r);

        a.Should().Contain(x => x.Level == ActionLevel.Medium && x.Issue == "2 stash(es) left over");
        a.Should().Contain(x => x.Level == ActionLevel.Low && x.Issue == "2 local branch(es) whose remote was deleted: old-1, old-2");
        a.Should().Contain(x => x.Level == ActionLevel.Low && x.Issue == "Last fetch 5 days ago");
    }

    [Fact]
    public void Tracking_branch_not_checked_out_is_info()
    {
        var a = Actions(Insight.Of("api", tracking: new BranchTracking("feature/7", true, false, false, null, null)));

        a.Single().Level.Should().Be(ActionLevel.Info);
    }

    [Fact]
    public void Actions_are_sorted_by_level_then_repo_and_kpis_count_them()
    {
        var report = PmReportBuilder.Build("/ws",
        [
            Insight.Of("zeta", behind: 1),
            Insight.Of("alpha", ahead: 1),
            Insight.Of("beta", changes: 1, oldest: Make.Now),
            Insight.Of("clean"),
        ], new ReportOptions(), Make.Now);

        report.Actions.Select(a => (a.Level, a.Repo)).Should().Equal(
            (ActionLevel.High, "alpha"), (ActionLevel.Medium, "beta"), (ActionLevel.Medium, "zeta"));
        Kpi(report, "Repos").Should().Be(4);
        Kpi(report, "Need attention").Should().Be(3);
        Kpi(report, "High priority items").Should().Be(1);
        Kpi(report, "Unpushed repos").Should().Be(1);
        Kpi(report, "Behind origin").Should().Be(1);
    }

    [Fact]
    public void Commits_are_deduplicated_and_mine_are_found_by_email()
    {
        var shared = Insight.Commit("api", "aaa111", "Me", "me@example.com", 2, "feat: login");
        var report = PmReportBuilder.Build("/ws",
        [
            Insight.Of("api", commits: [shared, Insight.Commit("api", "bbb222", "Other", "o@example.com", 5)]),
            Insight.Of("api-clone", commits: [shared]),
        ], new ReportOptions(), Make.Now);

        report.Commits.Should().HaveCount(2);
        report.MyCommits.Single().Subject.Should().Be("feat: login");
        ReportText.DailyReport(report).Should().Contain("api").And.Contain("- feat: login");
        ReportText.Html(report).Should().Contain("<h1>Workspace PM Report</h1>").And.Contain("feat: login");
    }

    private static int Kpi(WorkspaceReport r, string name) => r.Kpis.Single(k => k.Name == name).Value;
}

public class PmReportViewModelTests
{
    private sealed class FakeReportService(WorkspaceReport report) : IPmReportService
    {
        public ReportOptions? LastOptions { get; private set; }

        public Task<WorkspaceReport> BuildAsync(string workspaceRoot, ReportOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(report);
        }
    }

    private static (PmReportViewModel Vm, FakeReportService Service, FakeDialogs Dialogs, MemorySettings Settings) Create(AppSettings? settings = null)
    {
        var report = PmReportBuilder.Build("/ws",
        [
            Insight.Of("api", ahead: 1, commits: [Insight.Commit("api", "aaa111", "Me", "me@example.com", 2, "fix: x")],
                tracking: new BranchTracking("feature/7", true, true, false, 0, 0)),
            Insight.Of("site", lastFetch: Make.Now.AddDays(-10)),
        ], new ReportOptions(7, "feature/7"), Make.Now);
        var service = new FakeReportService(report);
        var dialogs = new FakeDialogs(null);
        var store = new MemorySettings(settings ?? new AppSettings { WorkspaceRoot = "/ws", SyncBranch = "feature/7", ReportDays = 5 });
        return (new PmReportViewModel(service, store, dialogs, new FakePlatform(), NullLogger<PmReportViewModel>.Instance), service, dialogs, store);
    }

    [Fact]
    public async Task Generate_fills_every_section()
    {
        var (vm, service, _, _) = Create();

        await vm.OnFirstShownAsync();

        service.LastOptions.Should().Be(new ReportOptions(5, "feature/7"));
        vm.HasReport.Should().BeTrue();
        vm.Kpis.Should().NotBeEmpty();
        vm.Actions.Should().ContainSingle(a => a.Repo == "api" && a.Level == ActionLevel.High);
        vm.Tracking.Single().CheckedOut.Should().Be("no (on main)");
        vm.DailyReport.Should().Contain("fix: x");
        vm.Team.Single().Author.Should().Be("Me");
    }

    [Fact]
    public async Task Low_priority_and_info_items_are_hidden_until_asked()
    {
        var (vm, _, _, _) = Create();
        await vm.GenerateAsync();
        var visible = vm.Actions.Count;

        vm.ShowLowAndInfo = true;

        vm.Actions.Count.Should().BeGreaterThan(visible);
        vm.Actions.Should().Contain(a => a.Level == ActionLevel.Low);
    }

    [Fact]
    public async Task Without_workspace_asks_to_choose_one()
    {
        var (vm, service, _, _) = Create(new AppSettings());

        await vm.GenerateAsync();

        service.LastOptions.Should().BeNull();
        vm.StatusText.Should().Contain("Choose the workspace folder");
    }

    [Fact]
    public async Task Copy_daily_report_and_export_html()
    {
        var (vm, _, dialogs, _) = Create();
        await vm.GenerateAsync();
        var file = Path.Combine(Path.GetTempPath(), $"wm-report-{Guid.NewGuid():N}.html");
        dialogs.SavePath = file;

        await vm.CopyDailyReportCommand.ExecuteAsync(null);
        await vm.ExportHtmlCommand.ExecuteAsync(null);

        dialogs.Clipboard.Should().Contain("fix: x");
        File.ReadAllText(file).Should().Contain("Workspace PM Report");
        File.Delete(file);
    }
}
