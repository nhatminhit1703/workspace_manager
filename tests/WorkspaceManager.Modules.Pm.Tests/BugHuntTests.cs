using WorkspaceManager.Core.Reports;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Tests;

public class BugHuntEngineTests
{
    private static BranchDiff Diff(
        Dictionary<string, char>? changed = null,
        string[]? tree = null,
        AddedLine[]? added = null,
        CommitInfo[]? commits = null) =>
        new("api", "feature/x", "origin/main", changed ?? [], (tree ?? []).ToHashSet(), added ?? [], commits ?? []);

    private static List<Finding> Run(BranchDiff d, BugHuntOptions? o = null) => BugHuntEngine.Run([d], o ?? new BugHuntOptions());

    [Theory]
    [InlineData("src/a.php", "    var_dump($user);", "debug", Severity.Medium)]
    [InlineData("src/a.js", "  console.log(order);", "debug", Severity.Low)]
    [InlineData("src/a.ts", "const url = 'http://localhost:8080/api';", "hardcoded-host", Severity.High)]
    [InlineData("src/Repo.php", "$sql = \"SELECT * FROM users WHERE id = \" . $id;", "sql-concat", Severity.High)]
    [InlineData("src/A.java", "} catch (Exception e) {}", "empty-catch", Severity.Medium)]
    [InlineData("config/app.env.example", "API_KEY=your_api_key", "placeholder", Severity.Medium)]
    public void Line_rules(string file, string line, string rule, Severity severity)
    {
        var f = Run(Diff(added: [new AddedLine(file, 12, line)])).Single(x => x.Rule == rule);

        f.Severity.Should().Be(severity);
        f.Where.Should().Be($"{file}:12");
        f.Audience.Should().Be(Audience.Dev);
    }

    [Fact]
    public void Comments_tests_and_minified_files_are_not_scanned_for_code_rules()
    {
        var f = Run(Diff(added:
        [
            new AddedLine("src/a.js", 1, "// console.log(x)"),
            new AddedLine("tests/a.test.js", 1, "console.log(x)"),
            new AddedLine("dist/a.min.js", 1, "console.log(x)"),
        ]));

        f.Should().BeEmpty();
    }

    [Fact]
    public void Extra_host_pattern_from_the_settings()
    {
        var f = Run(Diff(added: [new AddedLine("src/a.js", 3, "fetch('https://staging.example.com/x')")]), new BugHuntOptions { ExtraHostPattern = @"staging\.example\.com" });

        f.Should().ContainSingle(x => x.Rule == "hardcoded-host");
    }

    [Fact]
    public void Js_changed_without_rebuilding_its_min_file()
    {
        var f = Run(Diff(new Dictionary<string, char> { ["web/app.js"] = 'M' }, ["web/app.js", "web/app.min.js"])).Single();

        f.Rule.Should().Be("stale-min");
        f.Audience.Should().Be(Audience.Business);
        f.Vi.Expected.Should().Contain(".min");
        BugHuntEngine.ToTsv([f], vietnamese: true).Should().Contain("【Điều kiện】").And.Contain("【Kỳ vọng】");
        BugHuntEngine.ToTsv([f], vietnamese: false).Should().Contain("[Expected]");
    }

    [Fact]
    public void Desktop_changed_but_not_its_mobile_twin()
    {
        var f = Run(Diff(new Dictionary<string, char> { ["views/desktop/cart.html"] = 'M' }, ["views/desktop/cart.html", "views/mobile/cart.html"]));

        f.Should().ContainSingle(x => x.Rule == "device-parity" && x.Where == "views/mobile/cart.html");
    }

    [Fact]
    public void Source_changed_without_any_test()
    {
        var f = Run(Diff(new Dictionary<string, char> { ["src/Service.cs"] = 'M' }, ["src/Service.cs", "tests/ServiceTests.cs"]));

        f.Should().ContainSingle(x => x.Rule == "no-test");
    }

    [Fact]
    public void Risky_commits_become_regression_cases()
    {
        var f = Run(Diff(commits:
        [
            new CommitInfo("api", "a1", "a1", DateTimeOffset.Now, "x", "x@example.com", "fix: login session expires"),
            new CommitInfo("api", "b2", "b2", DateTimeOffset.Now, "x", "x@example.com", "feat: cart badge"),
            new CommitInfo("api", "c3", "c3", DateTimeOffset.Now, "x", "x@example.com", "docs: readme"),
        ]));

        f.Select(x => (x.Severity, x.Where)).Should().Equal((Severity.Critical, "a1"), (Severity.High, "b2"));
    }

    [Fact]
    public void Findings_are_capped_per_rule_and_sorted_by_severity()
    {
        var lines = Enumerable.Range(1, 40).Select(i => new AddedLine("src/a.js", i, "console.log(i)")).Append(new AddedLine("src/b.php", 1, "dd($x);")).ToArray();

        var f = Run(Diff(added: lines), new BugHuntOptions { MaxPerRule = 5 });

        f.Should().HaveCount(5);
        f[0].Severity.Should().Be(Severity.Medium, "the strong debug finding sorts before the low ones");
    }
}
