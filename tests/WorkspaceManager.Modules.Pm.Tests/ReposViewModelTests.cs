using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Repos;

namespace WorkspaceManager.Modules.Pm.Tests;

public class ReposViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ws");

    private static FakeReader Reader() => new(p => Path.GetFileName(p) switch
    {
        "api" => Make.Status(p, "feature/1", changes: 3, ahead: 0, behind: 2),
        "site" => Make.Status(p, "main"),
        "broken" => Make.Status(p, error: "fatal: not a git repository"),
        _ => Make.Status(p, "dev", ahead: null, behind: null),
    });

    [Fact]
    public async Task Refresh_reads_every_repository_and_summarises()
    {
        var settings = new MemorySettings(new AppSettings { WorkspaceRoot = Root, SelectedRepos = ["site"] });
        var vm = Make.ViewModel(new FakeScanner("api", "site", "broken", "tool"), Reader(), settings);

        await vm.OnFirstShownAsync();

        vm.Repos.Select(r => r.Name).Should().Equal("api", "site", "broken", "tool");
        vm.Repos.Single(r => r.Name == "site").IsSelected.Should().BeTrue("the selection is restored from the settings");
        vm.Repos.Single(r => r.Name == "api").Sync.Should().Be("behind 2");
        vm.Repos.Single(r => r.Name == "tool").Sync.Should().Be("no upstream");
        vm.StatusText.Should().Be("4 repositories | 1 with uncommitted changes | 1 behind upstream | 1 with errors");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Without_workspace_nothing_is_read()
    {
        var reader = Reader();
        var vm = Make.ViewModel(new FakeScanner("api"), reader, new MemorySettings());

        await vm.OnFirstShownAsync();

        vm.HasWorkspace.Should().BeFalse();
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Filter_matches_repository_or_branch()
    {
        var vm = Make.ViewModel(new FakeScanner("api", "site", "tool"), Reader(), new MemorySettings(new AppSettings { WorkspaceRoot = Root }));
        await vm.RefreshAsync();

        vm.Filter = "feature";
        vm.Repos.Select(r => r.Name).Should().Equal("api");

        vm.Filter = "SIT";
        vm.Repos.Select(r => r.Name).Should().Equal("site");

        vm.Filter = string.Empty;
        vm.Repos.Should().HaveCount(3);
    }

    [Fact]
    public async Task Select_helpers_tick_rows_and_save_the_selection()
    {
        var settings = new MemorySettings(new AppSettings { WorkspaceRoot = Root });
        var vm = Make.ViewModel(new FakeScanner("api", "site", "tool"), Reader(), settings);
        await vm.RefreshAsync();

        vm.SelectAll("changes");
        settings.Current.SelectedRepos.Should().Equal("api");

        vm.SelectAll("all");
        settings.Current.SelectedRepos.Should().HaveCount(3);

        vm.SelectAll("none");
        settings.Current.SelectedRepos.Should().BeEmpty();
    }

    [Fact]
    public async Task Choosing_a_workspace_saves_it_and_refreshes()
    {
        var settings = new MemorySettings();
        var reader = Reader();
        var vm = Make.ViewModel(new FakeScanner("api"), reader, settings, new FakeDialogs(Root));

        await vm.ChooseWorkspaceAsync();

        settings.Current.WorkspaceRoot.Should().Be(Root);
        vm.WorkspaceRoot.Should().Be(Root);
        reader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancelled_folder_dialog_changes_nothing()
    {
        var settings = new MemorySettings(new AppSettings { WorkspaceRoot = Root });
        var vm = Make.ViewModel(new FakeScanner("api"), Reader(), settings, new FakeDialogs(null));

        await vm.ChooseWorkspaceAsync();

        vm.WorkspaceRoot.Should().Be(Root);
        settings.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Open_commands_use_the_selected_row()
    {
        var platform = new FakePlatform();
        var vm = Make.ViewModel(new FakeScanner("api"), Reader(), new MemorySettings(new AppSettings { WorkspaceRoot = Root }), platform: platform);
        await vm.RefreshAsync();
        vm.SelectedRepo = vm.Repos[0];

        vm.OpenFolderCommand.Execute(null);
        vm.OpenTerminalCommand.Execute(null);

        platform.Opened.Should().Equal("folder:" + Path.Combine(Root, "api"), "terminal:" + Path.Combine(Root, "api"));
    }
}

public class RepoRowTests
{
    [Theory]
    [InlineData(0, 0, "up to date")]
    [InlineData(1, 0, "ahead 1")]
    [InlineData(0, 4, "behind 4")]
    [InlineData(2, 3, "behind 3, ahead 2")]
    public void Sync_text_matches_the_old_tool(int ahead, int behind, string expected) =>
        RepoRowViewModel.FormatSync(new BranchInfo("main", "x", "origin/main", ahead, behind)).Should().Be(expected);

    [Fact]
    public void Detached_head_shows_the_short_commit()
    {
        var s = Make.Status("/r/x") with { Branch = new BranchInfo(null, "abcdef1234567", null, null, null) };

        new RepoRowViewModel(s, Make.Now).Branch.Should().Be("(detached abcdef1)");
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(5, "5 min ago")]
    [InlineData(180, "3 h ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 12, "12 days ago")]
    [InlineData(60 * 24 * 120, "3 months ago")]
    [InlineData(60 * 24 * 800, "2 years ago")]
    public void Relative_time(double minutesAgo, string expected) =>
        RelativeTime.Format(Make.Now.AddMinutes(-minutesAgo), Make.Now).Should().Be(expected);
}
