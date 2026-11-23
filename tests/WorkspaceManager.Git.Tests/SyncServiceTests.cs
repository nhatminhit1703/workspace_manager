using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git.Tests;

/// <summary>Bulk sync on real throw-away repositories: an "origin" and a clone of it.</summary>
public sealed class SyncServiceTests : IDisposable
{
    private readonly TestRepo _origin;
    private readonly TestRepo _clone;

    public SyncServiceTests()
    {
        _origin = new TestRepo("origin").Init("master").Commit("c1", "a.txt");
        _origin.Git("branch", "feature/x");
        _clone = new TestRepo("clone", _origin.Root);
        TestRepo.RunGit(_origin.Root, "clone", "-q", _origin.Path_, _clone.Path_);
    }

    private static SyncService Service() => new(new GitRunner(TestRepo.IsolatedOptions()), new RepoLocks());

    private Task<IReadOnlyList<SyncResult>> Run(SyncRequest request) =>
        Service().RunAsync(request, [_clone.Path_], cancellationToken: TestContext.Current.CancellationToken);

    private string CloneHead() => _clone.Git("rev-parse", "--short", "HEAD").Trim();

    private string CloneBranch() => _clone.Git("branch", "--show-current").Trim();

    [Fact]
    public async Task Fetch_only_does_not_move_branches()
    {
        var head = CloneHead();
        _origin.Commit("c2");

        var r = (await Run(new SyncRequest(SyncMode.FetchOnly))).Single();

        r.Outcome.Should().Be(SyncOutcome.Fetched);
        CloneHead().Should().Be(head);
        _clone.Git("rev-list", "--count", "HEAD..origin/master").Trim().Should().Be("1");
    }

    [Fact]
    public async Task Auto_picks_master_and_fast_forwards()
    {
        _origin.Commit("c2");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull))).Single();

        r.Outcome.Should().Be(SyncOutcome.Updated);
        r.Target.Should().Be("master");
        r.After.Should().NotBe(r.Before);
        _clone.Git("rev-list", "--count", "HEAD..origin/master").Trim().Should().Be("0");
    }

    [Fact]
    public async Task Auto_prefers_production_when_it_exists()
    {
        _origin.Git("branch", "production");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull))).Single();

        r.Target.Should().Be("production");
        CloneBranch().Should().Be("production");
    }

    [Fact]
    public async Task Checkout_named_branch_creates_a_tracking_branch()
    {
        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, "feature/x"))).Single();

        r.Outcome.Should().Be(SyncOutcome.Updated);
        r.Message.Should().Be("created from origin/feature/x (switched from 'master')");
        CloneBranch().Should().Be("feature/x");
        _clone.Git("rev-parse", "--abbrev-ref", "feature/x@{upstream}").Trim().Should().Be("origin/feature/x");
    }

    [Fact]
    public async Task Missing_branch_is_skipped()
    {
        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, "nope"))).Single();

        r.Outcome.Should().Be(SyncOutcome.Skipped);
        r.Message.Should().Be("origin/nope not found");
    }

    [Fact]
    public async Task Uncommitted_changes_are_skipped_without_stash()
    {
        _clone.Write("a.txt", "local edit");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, "feature/x"))).Single();

        r.Outcome.Should().Be(SyncOutcome.Skipped);
        CloneBranch().Should().Be("master");
        File.ReadAllText(Path.Combine(_clone.Path_, "a.txt")).Should().Be("local edit");
    }

    [Fact]
    public async Task Stash_switches_and_reapplies_local_changes()
    {
        _clone.Write("a.txt", "local edit");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, "feature/x", StashLocalChanges: true))).Single();

        r.IsOk.Should().BeTrue();
        r.Warning.Should().Be("local changes re-applied");
        CloneBranch().Should().Be("feature/x");
        File.ReadAllText(Path.Combine(_clone.Path_, "a.txt")).Should().Be("local edit");
        _clone.Git("stash", "list").Trim().Should().BeEmpty();
    }

    [Fact]
    public async Task Keep_current_branch_only_moves_the_target_ref()
    {
        _clone.Git("checkout", "-q", "-b", "work");
        _clone.Git("branch", "-q", "--track", "feature/x", "origin/feature/x");
        _origin.Git("checkout", "-q", "feature/x");
        _origin.Commit("on feature");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, "feature/x", KeepCurrentBranch: true))).Single();

        r.Outcome.Should().Be(SyncOutcome.RefUpdated);
        CloneBranch().Should().Be("work");
        _clone.Git("rev-list", "--count", "feature/x..origin/feature/x").Trim().Should().Be("0");
    }

    [Fact]
    public async Task Diverged_branch_fails_and_is_never_merged()
    {
        _origin.Commit("remote change");
        _clone.Commit("local change");
        var head = CloneHead();

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, SyncRequest.Current))).Single();

        r.Outcome.Should().Be(SyncOutcome.Failed);
        r.Message.Should().Contain("not a fast-forward");
        CloneHead().Should().Be(head);
    }

    [Fact]
    public async Task Current_mode_fast_forwards_each_repository_on_its_branch()
    {
        _origin.Commit("c2");

        var r = (await Run(new SyncRequest(SyncMode.CheckoutAndPull, SyncRequest.Current))).Single();

        r.Outcome.Should().Be(SyncOutcome.Updated);
        r.Target.Should().Be("master");
    }

    [Fact]
    public async Task Progress_reports_every_repository()
    {
        var reported = new List<SyncResult>();
        var progress = new SyncProgress(reported);

        var results = await Service().RunAsync(new SyncRequest(SyncMode.FetchOnly), [_clone.Path_, _origin.Path_], progress, TestContext.Current.CancellationToken);

        results.Should().HaveCount(2);
        reported.Should().HaveCount(2);
        results.Single(x => x.Repo == "origin").Outcome.Should().Be(SyncOutcome.Failed, "origin has no remote named origin");
    }

    public void Dispose()
    {
        _clone.Dispose();
        _origin.Dispose();
    }

    private sealed class SyncProgress(List<SyncResult> sink) : IProgress<SyncResult>
    {
        public void Report(SyncResult value)
        {
            lock (sink)
            {
                sink.Add(value);
            }
        }
    }
}
