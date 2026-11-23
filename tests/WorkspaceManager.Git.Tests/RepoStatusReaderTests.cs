namespace WorkspaceManager.Git.Tests;

/// <summary>Integration tests: real git on throw-away repositories.</summary>
public class RepoStatusReaderTests
{
    private static RepoStatusReader Reader() => new(new GitRunner(TestRepo.IsolatedOptions()));

    [Fact]
    public async Task Clean_repository_without_remote()
    {
        using var repo = new TestRepo().Init().Commit("first");

        var s = await Reader().ReadAsync(repo.Path_, TestContext.Current.CancellationToken);

        s.Error.Should().BeNull();
        s.Name.Should().Be("repo");
        s.Branch.Head.Should().Be("main");
        s.Branch.HasUpstream.Should().BeFalse();
        s.Changes.Total.Should().Be(0);
        s.LastCommitDate.Should().NotBeNull();
        s.LastFetch.Should().BeNull();
        s.RemoteBranches.Should().BeEmpty();
    }

    [Fact]
    public async Task Counts_staged_unstaged_and_untracked_files_with_unicode_and_spaces()
    {
        using var repo = new TestRepo().Init().Commit("first", "a.txt").Commit("second", "b.txt");
        repo.Write("a.txt", "changed");
        repo.Write("b.txt", "staged");
        repo.Git("add", "b.txt");
        repo.Write("thư mục/tệp có dấu cách.txt", "new");

        var s = await Reader().ReadAsync(repo.Path_, TestContext.Current.CancellationToken);

        s.Changes.Staged.Should().Be(1);
        s.Changes.Unstaged.Should().Be(1);
        s.Changes.Untracked.Should().Be(1);
        s.Changes.Total.Should().Be(3);
    }

    [Fact]
    public async Task Ahead_and_behind_its_upstream_and_remote_branches()
    {
        using var origin = new TestRepo("origin").Init().Commit("c1");
        origin.Git("branch", "feature/a");
        using var clone = new TestRepo("clone", origin.Root);
        TestRepo.RunGit(origin.Root, "clone", "-q", origin.Path_, clone.Path_);

        origin.Commit("c2").Commit("c3");      // origin moves 2 ahead
        clone.Git("fetch", "-q");
        clone.Commit("local");                  // clone has 1 own commit

        var s = await Reader().ReadAsync(clone.Path_, TestContext.Current.CancellationToken);

        s.Branch.Upstream.Should().Be("origin/main");
        s.Branch.Ahead.Should().Be(1);
        s.Branch.Behind.Should().Be(2);
        s.RemoteBranches.Should().BeEquivalentTo(["main", "feature/a"]);
        s.HasRemoteBranch("feature/a").Should().BeTrue();
        s.LastFetch.Should().NotBeNull();
    }

    [Fact]
    public async Task Detached_head()
    {
        using var repo = new TestRepo().Init().Commit("c1").Commit("c2");
        repo.Git("checkout", "-q", "--detach", "HEAD~1");

        var s = await Reader().ReadAsync(repo.Path_, TestContext.Current.CancellationToken);

        s.Branch.IsDetached.Should().BeTrue();
        s.Error.Should().BeNull();
    }

    [Fact]
    public async Task Repository_without_commits()
    {
        using var repo = new TestRepo().Init();

        var s = await Reader().ReadAsync(repo.Path_, TestContext.Current.CancellationToken);

        s.Error.Should().BeNull();
        s.Branch.IsUnborn.Should().BeTrue();
        s.LastCommitDate.Should().BeNull();
    }

    [Fact]
    public async Task Folder_that_is_not_a_repository()
    {
        using var dir = new TestRepo();

        var s = await Reader().ReadAsync(dir.Path_, TestContext.Current.CancellationToken);

        s.HasError.Should().BeTrue();
    }

    [Fact]
    public void Scanner_finds_only_direct_child_working_trees_sorted()
    {
        using var b = new TestRepo("b-repo").Init();
        using var a = new TestRepo("A-repo", b.Root).Init();
        Directory.CreateDirectory(Path.Combine(b.Root, "not-a-repo"));

        var found = new WorkspaceScanner().FindRepositories(b.Root);

        found.Select(Path.GetFileName).Should().Equal("A-repo", "b-repo");
    }
}
