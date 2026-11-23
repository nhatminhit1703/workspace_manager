namespace WorkspaceManager.Git.Tests;

public class BranchDiffParserTests
{
    [Fact]
    public void Added_lines_with_new_line_numbers()
    {
        const string diff = """
            diff --git a/src/a.js b/src/a.js
            index 111..222 100644
            --- a/src/a.js
            +++ b/src/a.js
            @@ -3,0 +4,2 @@ function x() {
            +console.log(1);
            +console.log(2);
            @@ -10 +12 @@
            -old
            +new line
            diff --git a/gone.txt b/gone.txt
            deleted file mode 100644
            --- a/gone.txt
            +++ /dev/null
            @@ -1 +0,0 @@
            -bye
            """;

        var lines = BranchDiffReader.ParseAddedLines(diff);

        lines.Select(l => (l.File, l.Line, l.Text)).Should().Equal(
            ("src/a.js", 4, "console.log(1);"), ("src/a.js", 5, "console.log(2);"), ("src/a.js", 12, "new line"));
    }

    [Fact]
    public void Name_status_with_nul_separators()
    {
        BranchDiffReader.ParseNameStatus("M\0src/a.js\0A\0docs/new file.md\0").Should().BeEquivalentTo(
            new Dictionary<string, char> { ["src/a.js"] = 'M', ["docs/new file.md"] = 'A' });
    }
}

public sealed class BranchDiffReaderTests : IDisposable
{
    private readonly TestRepo _origin = new TestRepo("origin").Init("main").Commit("base", "app.js");
    private readonly TestRepo _clone;

    public BranchDiffReaderTests()
    {
        _origin.Git("checkout", "-q", "-b", "feature/x");
        _origin.Write("app.js", "base\nconsole.log('debug');\n");
        _origin.Git("commit", "-q", "-am", "fix: login redirect");
        _origin.Git("checkout", "-q", "main");
        _clone = new TestRepo("clone", _origin.Root);
        TestRepo.RunGit(_origin.Root, "clone", "-q", _origin.Path_, _clone.Path_);
    }

    [Fact]
    public async Task Reads_changed_files_added_lines_and_commits_against_the_base()
    {
        var d = await new BranchDiffReader(new GitRunner(TestRepo.IsolatedOptions())).ReadAsync(_clone.Path_, "feature/x", TestContext.Current.CancellationToken);

        d.Should().NotBeNull();
        d!.BaseRef.Should().Be("origin/main");
        d.ChangedFiles.Should().ContainKey("app.js");
        d.AddedLines.Should().ContainSingle(l => l.File == "app.js" && l.Line == 2 && l.Text == "console.log('debug');");
        d.Commits.Single().Subject.Should().Be("fix: login redirect");
        d.TreeFiles.Should().Contain("app.js");
    }

    [Fact]
    public async Task Missing_branch_gives_null()
    {
        var d = await new BranchDiffReader(new GitRunner(TestRepo.IsolatedOptions())).ReadAsync(_clone.Path_, "nope", TestContext.Current.CancellationToken);

        d.Should().BeNull();
    }

    public void Dispose()
    {
        _clone.Dispose();
        _origin.Dispose();
    }
}
