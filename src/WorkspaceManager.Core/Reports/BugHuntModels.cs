namespace WorkspaceManager.Core.Reports;

/// <summary>What a branch changes in one repository, relative to its base branch.</summary>
public sealed record BranchDiff(
    string Repo,
    string Branch,
    string BaseRef,
    IReadOnlyDictionary<string, char> ChangedFiles,
    IReadOnlySet<string> TreeFiles,
    IReadOnlyList<AddedLine> AddedLines,
    IReadOnlyList<CommitInfo> Commits);

public sealed record AddedLine(string File, int Line, string Text);

public interface IBranchDiffReader
{
    /// <summary>The diff of origin/&lt;branch&gt; against production, else master, else main. Null when the branch or a base is missing.</summary>
    Task<BranchDiff?> ReadAsync(string repoPath, string branch, CancellationToken cancellationToken = default);
}

public enum Severity
{
    Critical,
    High,
    Medium,
    Low,
}

/// <summary>Business = a tester can check it in the UI; Dev = needs a code review.</summary>
public enum Audience
{
    Business,
    Dev,
}

/// <summary>A test case in the PCL format: precondition, steps, expected result.</summary>
public sealed record PclCase(string Title, string Precondition, string Steps, string Expected);

public sealed record Finding(Severity Severity, string Rule, string Repo, string Where, string What, Audience Audience, PclCase Vi, PclCase En);

/// <summary>Workspace-specific knobs, kept in the local settings.</summary>
public sealed record BugHuntOptions
{
    /// <summary>Extra regular expression of hosts that must not be hard-coded (staging servers...).</summary>
    public string? ExtraHostPattern { get; init; }

    /// <summary>Folder pairs that must change together, e.g. ("/desktop/", "/mobile/").</summary>
    public IReadOnlyList<(string A, string B)> DeviceParityPairs { get; init; } = [("/desktop/", "/mobile/"), ("/pc/", "/sp/")];

    public int MaxPerRule { get; init; } = 25;
}
