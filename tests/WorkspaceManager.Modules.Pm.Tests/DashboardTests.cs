using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Dashboard;

namespace WorkspaceManager.Modules.Pm.Tests;

internal sealed class FakeSetup : ISetupService
{
    public GitIdentity Identity { get; set; } = new("Me", "me@example.com");

    public List<RepoEntry> Cloned { get; } = [];

    public Task<string?> GitVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("git version 2.50.0");

    public Task<GitIdentity> GetIdentityAsync(CancellationToken cancellationToken = default) => Task.FromResult(Identity);

    public Task SetIdentityAsync(GitIdentity identity, CancellationToken cancellationToken = default)
    {
        Identity = identity;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncResult>> CloneMissingAsync(string workspaceRoot, IReadOnlyList<RepoEntry> entries, IProgress<SyncResult>? progress = null, CancellationToken cancellationToken = default)
    {
        Cloned.AddRange(entries);
        IReadOnlyList<SyncResult> r = entries.Select(e => new SyncResult(e.Name, SyncOutcome.Updated, "cloned")).ToList();
        foreach (var x in r)
        {
            progress?.Report(x);
        }

        return Task.FromResult(r);
    }
}

internal sealed class FakeNavigator : INavigator
{
    public List<string> Opened { get; } = [];

    public void Navigate(string navigationId) => Opened.Add(navigationId);
}

public sealed class DashboardViewModelTests : IDisposable
{
    private readonly string _ws = Directory.CreateTempSubdirectory("wm-dash").FullName;

    private (DashboardViewModel Vm, FakeSetup Setup, FakeDialogs Dialogs, MemorySettings Settings, FakeNavigator Nav, FakeSync Sync) Create(AppSettings settings)
    {
        var setup = new FakeSetup();
        var dialogs = new FakeDialogs(_ws);
        var store = new MemorySettings(settings);
        var nav = new FakeNavigator();
        var sync = new FakeSync();
        var vm = new DashboardViewModel(new StaticReports(), setup, sync, new FakeScanner("api"), store, dialogs, new FakeKeys(), nav,
            new FakePlatform(), NullLogger<DashboardViewModel>.Instance, new FixedTime(Make.Now));
        return (vm, setup, dialogs, store, nav, sync);
    }

    [Fact]
    public async Task New_machine_shows_the_setup_checklist()
    {
        var (vm, _, _, _, _, _) = Create(new AppSettings());

        await vm.OnFirstShownAsync();

        vm.NeedsSetup.Should().BeTrue();
        vm.Setup.Single(s => s.Text.StartsWith("Workspace folder", StringComparison.Ordinal)).Done.Should().BeFalse();
        vm.HasReport.Should().BeFalse();
    }

    [Fact]
    public async Task Configured_workspace_shows_kpis_actions_and_my_work()
    {
        var (vm, _, _, _, _, _) = Create(new AppSettings { WorkspaceRoot = _ws });

        await vm.OnFirstShownAsync();

        vm.NeedsSetup.Should().BeFalse();
        vm.Kpis.Should().NotBeEmpty();
        vm.Actions.Should().ContainSingle(a => a.Repo == "api");
        vm.MyWork.Should().Contain("feat: login");
    }

    [Fact]
    public async Task Repository_list_is_saved_locally_and_missing_ones_are_cloned()
    {
        Directory.CreateDirectory(Path.Combine(_ws, "api"));
        var (vm, setup, _, settings, _, _) = Create(new AppSettings { WorkspaceRoot = _ws });
        vm.RepoListText = "api | https://example.com/api.git\nhttps://example.com/site.git";

        await vm.SaveRepoListCommand.ExecuteAsync(null);
        await vm.CloneMissingCommand.ExecuteAsync(null);

        settings.Current.RepoList.Select(r => r.Name).Should().Equal("api", "site");
        setup.Cloned.Select(c => c.Name).Should().Equal("site");
        vm.Log.Should().ContainSingle(l => l.Repo == "site" && l.Message == "cloned");
    }

    [Fact]
    public async Task Git_identity_is_asked_and_saved()
    {
        var (vm, setup, dialogs, _, _, _) = Create(new AppSettings { WorkspaceRoot = _ws });
        setup.Identity = new GitIdentity(null, null);
        dialogs.Texts.Enqueue("New Name");
        dialogs.Texts.Enqueue("new@example.com");

        await vm.SetIdentityCommand.ExecuteAsync(null);

        setup.Identity.Should().Be(new GitIdentity("New Name", "new@example.com"));
    }

    [Fact]
    public async Task Shortcuts_and_fetch_all()
    {
        var (vm, _, _, _, nav, sync) = Create(new AppSettings { WorkspaceRoot = _ws });

        vm.OpenCommand.Execute("pm.report");
        await vm.FetchAllCommand.ExecuteAsync(null);

        nav.Opened.Should().Equal("pm.report");
        sync.Runs.Single().Request.Mode.Should().Be(SyncMode.FetchOnly);
    }

    public void Dispose() => Directory.Delete(_ws, recursive: true);
}
