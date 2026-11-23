using WorkspaceManager.Core.Reports;
using WorkspaceManager.Git.Parsing;

namespace WorkspaceManager.Git.Tests;

public class HistoryParserTests
{
    private const char S = HistoryParser.Sep;

    [Fact]
    public void Log_lines_with_unit_separators()
    {
        var c = HistoryParser.ParseLog($"abc123{S}abc{S}2026-11-20T10:15:00+07:00{S}An{S}an@example.com{S}feat: add x\n", "api").Single();

        c.Should().Be(new CommitInfo("api", "abc123", "abc", new DateTimeOffset(2026, 11, 20, 10, 15, 0, TimeSpan.FromHours(7)), "An", "an@example.com", "feat: add x"));
    }

    [Theory]
    [InlineData("", false, 0, 0)]
    [InlineData("[gone]", true, 0, 0)]
    [InlineData("[ahead 2]", false, 2, 0)]
    [InlineData("[behind 13]", false, 0, 13)]
    [InlineData("[ahead 2, behind 1]", false, 2, 1)]
    public void Branch_tracking(string track, bool gone, int ahead, int behind)
    {
        var b = HistoryParser.ParseBranches($"feature/x{S}origin/feature/x{S}{track}\n").Single();

        b.UpstreamGone.Should().Be(gone);
        b.Ahead.Should().Be(ahead);
        b.Behind.Should().Be(behind);
    }

    [Fact]
    public void Branch_without_upstream()
    {
        HistoryParser.ParseBranches($"local-only{S}{S}\n").Single().Upstream.Should().BeNull();
    }

    [Fact]
    public void Stash_entries()
    {
        var s = HistoryParser.ParseStashes($"stash@{{0}}{S}2026-11-01T08:00:00+00:00{S}On main: wip\n").Single();

        s.Name.Should().Be("stash@{0}");
        s.Message.Should().Be("On main: wip");
        s.Date.Should().NotBeNull();
    }
}

/// <summary>Integration: the report data of a real throw-away repository.</summary>
public sealed class RepoInsightReaderTests : IDisposable
{
    private readonly TestRepo _origin = new TestRepo("origin").Init("main").Commit("c1", "a.txt");
    private readonly TestRepo _clone;

    public RepoInsightReaderTests()
    {
        _origin.Git("branch", "feature/old");
        _clone = new TestRepo("clone", _origin.Root);
        TestRepo.RunGit(_origin.Root, "clone", "-q", _origin.Path_, _clone.Path_);
    }

    private static RepoInsightReader Reader()
    {
        var git = new GitRunner(TestRepo.IsolatedOptions());
        return new RepoInsightReader(git, new RepoStatusReader(git));
    }

    [Fact]
    public async Task Collects_stashes_branches_commits_and_tracking()
    {
        _clone.Git("branch", "--track", "feature/old", "origin/feature/old");
        _origin.Git("branch", "-D", "feature/old");
        _clone.Git("fetch", "-q", "--prune");
        _clone.Write("a.txt", "wip");
        _clone.Git("stash", "push", "-q", "-m", "wip");
        _clone.Commit("local work", "b.txt");
        _clone.Git("config", "user.email", "me@example.com");

        var r = await Reader().ReadAsync(_clone.Path_, new ReportOptions(7, "main"), DateTimeOffset.Now, TestContext.Current.CancellationToken);

        r.Stashes.Should().ContainSingle().Which.Message.Should().Contain("wip");
        r.LocalBranches.Single(b => b.Name == "feature/old").UpstreamGone.Should().BeTrue();
        r.LocalBranches.Single(b => b.Name == "main").Ahead.Should().Be(1);
        r.Commits.Select(c => c.Subject).Should().Contain(["c1", "local work"]);
        r.UserEmail.Should().Be("me@example.com");
        r.Tracking.Should().Be(new BranchTracking("main", true, true, true, 1, 0));
    }

    [Fact]
    public async Task Oldest_changed_file_time_is_reported()
    {
        _clone.Write("new.txt", "x");
        var old = DateTime.Now.AddDays(-10);
        File.SetLastWriteTime(Path.Combine(_clone.Path_, "new.txt"), old);

        var r = await Reader().ReadAsync(_clone.Path_, new ReportOptions(), DateTimeOffset.Now, TestContext.Current.CancellationToken);

        r.OldestChangedFile!.Value.LocalDateTime.Should().BeCloseTo(old, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Untracked_folder_counts_with_its_own_time()
    {
        _clone.Write("notes/a.txt", "x");
        var old = DateTime.Now.AddDays(-30);
        File.SetLastWriteTime(Path.Combine(_clone.Path_, "notes", "a.txt"), DateTime.Now);
        Directory.SetLastWriteTime(Path.Combine(_clone.Path_, "notes"), old);

        var r = await Reader().ReadAsync(_clone.Path_, new ReportOptions(), DateTimeOffset.Now, TestContext.Current.CancellationToken);

        r.OldestChangedFile!.Value.LocalDateTime.Should().BeCloseTo(old, TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _clone.Dispose();
        _origin.Dispose();
    }
}
