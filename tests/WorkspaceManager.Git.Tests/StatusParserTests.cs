using WorkspaceManager.Git.Parsing;

namespace WorkspaceManager.Git.Tests;

public class StatusParserTests
{
    private const string Oid = "0123456789abcdef0123456789abcdef01234567";

    private static string Z(params string[] records) => string.Concat(records.Select(r => r + "\0"));

    [Fact]
    public void Branch_with_upstream_ahead_and_behind()
    {
        var s = StatusParser.Parse(Z(
            $"# branch.oid {Oid}",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -5"));

        s.Branch.Head.Should().Be("feature/x");
        s.Branch.CommitId.Should().Be(Oid);
        s.Branch.Upstream.Should().Be("origin/feature/x");
        s.Branch.Ahead.Should().Be(2);
        s.Branch.Behind.Should().Be(5);
        s.Branch.IsDetached.Should().BeFalse();
        s.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Detached_head_without_upstream()
    {
        var s = StatusParser.Parse(Z($"# branch.oid {Oid}", "# branch.head (detached)"));

        s.Branch.IsDetached.Should().BeTrue();
        s.Branch.HasUpstream.Should().BeFalse();
        s.Branch.Ahead.Should().BeNull();
    }

    [Fact]
    public void New_repository_has_no_commit()
    {
        var s = StatusParser.Parse(Z("# branch.oid (initial)", "# branch.head main"));

        s.Branch.IsUnborn.Should().BeTrue();
        s.Branch.Head.Should().Be("main");
    }

    [Fact]
    public void Ordinary_renamed_unmerged_untracked_and_ignored_entries()
    {
        var s = StatusParser.Parse(Z(
            $"# branch.oid {Oid}",
            "# branch.head main",
            $"1 M. N... 100644 100644 100644 {Oid} {Oid} src/staged.cs",
            $"1 .M N... 100644 100644 100644 {Oid} {Oid} docs/my file with spaces.md",
            $"1 MM N... 100644 100644 100644 {Oid} {Oid} both.txt",
            $"2 R. N... 100644 100644 100644 {Oid} {Oid} R100 new name.txt",
            "old name.txt",
            $"u UU N... 100644 100644 100644 100644 {Oid} {Oid} {Oid} conflict.txt",
            "? tiếng việt/ảnh.png",
            "! bin/"));

        s.Entries.Should().HaveCount(7);
        s.Entries[1].Path.Should().Be("docs/my file with spaces.md");
        s.Entries[3].Should().Be(new StatusEntry(StatusEntryKind.RenamedOrCopied, 'R', '.', "new name.txt", "old name.txt"));
        s.Entries[4].Kind.Should().Be(StatusEntryKind.Unmerged);
        s.Entries[5].Path.Should().Be("tiếng việt/ảnh.png");

        var c = s.Counts;
        c.Staged.Should().Be(3);     // M., MM, R.
        c.Unstaged.Should().Be(2);   // .M, MM
        c.Untracked.Should().Be(1);
        c.Conflicted.Should().Be(1);
        c.Total.Should().Be(6, "ignored files are not changes and a path counts once");
    }

    [Fact]
    public void Empty_output_gives_unknown_branch()
    {
        var s = StatusParser.Parse(string.Empty);

        s.Entries.Should().BeEmpty();
        s.Branch.Head.Should().BeNull();
    }

    [Fact]
    public void Unknown_record_is_rejected()
    {
        var act = () => StatusParser.Parse(Z("X something"));

        act.Should().Throw<FormatException>();
    }
}

public class RefParserTests
{
    [Fact]
    public void Remote_branches_without_prefix_and_head()
    {
        var set = RefParser.ParseRemoteBranches(
            "refs/remotes/origin/HEAD\nrefs/remotes/origin/main\r\nrefs/remotes/origin/global/2026.001_feature\nrefs/remotes/upstream/main\n",
            "origin");

        set.Should().BeEquivalentTo(["main", "global/2026.001_feature"]);
    }
}
