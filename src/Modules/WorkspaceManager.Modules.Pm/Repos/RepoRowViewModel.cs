using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Modules.Pm.Repos;

/// <summary>One line of the repository grid.</summary>
public sealed partial class RepoRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public RepoRowViewModel(RepoStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(status);
        Status = status;
        var b = status.Branch;
        Branch = status.HasError ? string.Empty
            : b.IsDetached ? $"(detached {Short(b.CommitId)})"
            : b.Head ?? string.Empty;
        Changes = status.Changes.Total;
        Sync = FormatSync(b);
        LastCommit = RelativeTime.Format(status.LastCommitDate, now);
        LastFetch = status.LastFetch?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "never";
        IsBehind = b.Behind > 0;
        IsAhead = b.Ahead > 0;
    }

    public RepoStatus Status { get; }

    public string Name => Status.Name;

    public string Path => Status.Path;

    public string Branch { get; }

    public int Changes { get; }

    public bool HasChanges => Changes > 0;

    public string Sync { get; }

    public bool IsBehind { get; }

    public bool IsAhead { get; }

    public string LastCommit { get; }

    public string LastFetch { get; }

    public string? Error => Status.Error;

    public bool HasError => Status.HasError;

    /// <summary>Path, plus the error when reading the repository failed.</summary>
    public string ToolTip => HasError ? $"{Path}\n{Error}" : Path;

    /// <summary>Same wording as the PowerShell tool: "up to date", "behind 2, ahead 1", "no upstream".</summary>
    public static string FormatSync(BranchInfo branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.Ahead is null || branch.Behind is null)
        {
            return "no upstream";
        }

        if (branch.Ahead == 0 && branch.Behind == 0)
        {
            return "up to date";
        }

        var parts = new List<string>(2);
        if (branch.Behind > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"behind {branch.Behind}"));
        }

        if (branch.Ahead > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"ahead {branch.Ahead}"));
        }

        return string.Join(", ", parts);
    }

    private static string Short(string? id) => id is { Length: > 7 } ? id[..7] : id ?? string.Empty;
}
