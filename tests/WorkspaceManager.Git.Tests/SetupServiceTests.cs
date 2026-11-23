using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git.Tests;

public class RepoEntryTests
{
    [Fact]
    public void Parses_name_and_url_or_bare_url_and_skips_comments()
    {
        var list = RepoEntry.ParseList("""
            # team repositories
            api | https://example.com/team/api.git

            https://example.com/team/site.git
            git@example.com:team/tools.git
            """);

        list.Should().Equal(
            new RepoEntry("api", "https://example.com/team/api.git"),
            new RepoEntry("site", "https://example.com/team/site.git"),
            new RepoEntry("tools", "git@example.com:team/tools.git"));
        RepoEntry.Format(list).Should().StartWith("api | https://example.com/team/api.git\nsite | ");
    }
}

public sealed class SetupServiceTests : IDisposable
{
    private readonly TestRepo _origin = new TestRepo("origin").Init().Commit("c1");
    private readonly string _globalConfig = Path.Combine(Path.GetTempPath(), "wm-tests", Guid.NewGuid().ToString("N") + ".gitconfig");

    private SetupService Service()
    {
        File.WriteAllText(_globalConfig, string.Empty);
        var o = TestRepo.IsolatedOptions();
        o.Environment["GIT_CONFIG_GLOBAL"] = _globalConfig;
        return new SetupService(new GitRunner(o));
    }

    [Fact]
    public async Task Clones_only_the_missing_repositories()
    {
        var ws = Path.Combine(_origin.Root, "ws");
        Directory.CreateDirectory(Path.Combine(ws, "existing"));
        var entries = new List<RepoEntry> { new("copy", _origin.Path_), new("existing", _origin.Path_), new("bad", Path.Combine(_origin.Root, "nope")) };

        var results = await Service().CloneMissingAsync(ws, entries, cancellationToken: TestContext.Current.CancellationToken);

        results.Select(r => (r.Repo, r.Outcome)).Should().Equal(("copy", SyncOutcome.Updated), ("existing", SyncOutcome.Skipped), ("bad", SyncOutcome.Failed));
        GitDirectory.IsWorkingTree(Path.Combine(ws, "copy")).Should().BeTrue();
    }

    [Fact]
    public async Task Reads_and_writes_the_global_identity()
    {
        var s = Service();
        (await s.GitVersionAsync(TestContext.Current.CancellationToken)).Should().StartWith("git version");
        (await s.GetIdentityAsync(TestContext.Current.CancellationToken)).IsComplete.Should().BeFalse();

        await s.SetIdentityAsync(new GitIdentity("Test User", "test@example.com"), TestContext.Current.CancellationToken);

        (await s.GetIdentityAsync(TestContext.Current.CancellationToken)).Should().Be(new GitIdentity("Test User", "test@example.com"));
    }

    public void Dispose()
    {
        _origin.Dispose();
        File.Delete(_globalConfig);
    }
}
