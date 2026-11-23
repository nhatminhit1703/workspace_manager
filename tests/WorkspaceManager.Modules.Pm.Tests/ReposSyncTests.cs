using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Repos;

namespace WorkspaceManager.Modules.Pm.Tests;

public class ReposSyncTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ws");

    private static FakeReader Reader() => new(p => Path.GetFileName(p) switch
    {
        "api" => Make.Status(p, "main") with { RemoteBranches = new HashSet<string> { "main", "feature/7" } },
        _ => Make.Status(p, "main"),
    });

    private static async Task<(ReposViewModel Vm, FakeSync Sync, FakeDialogs Dialogs, MemorySettings Settings)> Create(bool confirm = true)
    {
        var sync = new FakeSync();
        var dialogs = new FakeDialogs(null, confirm);
        var settings = new MemorySettings(new AppSettings { WorkspaceRoot = Root, SelectedRepos = ["api"] });
        var vm = Make.ViewModel(new FakeScanner("api", "site"), Reader(), settings, dialogs, sync: sync);
        await vm.RefreshAsync();
        return (vm, sync, dialogs, settings);
    }

    [Fact]
    public async Task Fetch_runs_on_ticked_repositories_and_logs_each_result()
    {
        var (vm, sync, _, _) = await Create();

        await vm.FetchAsync();

        sync.Runs.Should().ContainSingle();
        sync.Runs[0].Request.Mode.Should().Be(SyncMode.FetchOnly);
        sync.Runs[0].Paths.Select(Path.GetFileName).Should().Equal("api");
        vm.Log.Select(l => l.Repo).Should().Contain("api");
        vm.Log.Last().Message.Should().Be("1 ok, 0 skipped, 0 failed");
    }

    [Fact]
    public async Task Checkout_asks_for_confirmation_with_the_options()
    {
        var (vm, sync, dialogs, _) = await Create();
        vm.SyncBranch = "feature/7";
        vm.SyncStash = true;

        await vm.CheckoutAndPullAsync();

        dialogs.Questions.Single().Should().Contain("Branch: feature/7").And.Contain("Stash local changes: yes").And.Contain("api");
        sync.Runs.Single().Request.Should().Be(new SyncRequest(SyncMode.CheckoutAndPull, "feature/7", false, true));
    }

    [Fact]
    public async Task Cancelled_confirmation_runs_nothing()
    {
        var (vm, sync, _, _) = await Create(confirm: false);

        await vm.CheckoutAndPullAsync();

        sync.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_ticked_runs_nothing()
    {
        var (vm, sync, _, _) = await Create();
        vm.SelectAll("none");

        await vm.PullCurrentAsync();

        sync.Runs.Should().BeEmpty();
        vm.StatusText.Should().Be("Tick at least one repository.");
    }

    [Fact]
    public async Task Named_branch_marks_repositories_that_have_it_and_options_are_saved()
    {
        var (vm, _, _, settings) = await Create();

        vm.SyncBranch = "feature/7";

        vm.IsNamedBranch.Should().BeTrue();
        vm.Repos.Single(r => r.Name == "api").HasTargetBranch.Should().BeTrue();
        vm.Repos.Single(r => r.Name == "site").HasTargetBranch.Should().BeFalse();
        vm.BranchSuggestions.Should().StartWith(["auto", "current"]).And.Contain("feature/7");
        settings.Current.SyncBranch.Should().Be("feature/7");

        vm.SelectAll("has-branch");
        vm.Repos.Where(r => r.IsSelected).Select(r => r.Name).Should().Equal("api");
    }
}
