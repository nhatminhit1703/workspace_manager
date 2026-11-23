using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Core.Settings;

/// <summary>Per-user settings, stored outside the repository (see IPlatformService.AppDataDirectory).</summary>
public sealed class AppSettings
{
    /// <summary>Folder whose direct sub folders are the repositories of the workspace.</summary>
    public string? WorkspaceRoot { get; set; }

    /// <summary>Repositories ticked in the grid (bulk operations work on them).</summary>
    public List<string> SelectedRepos { get; set; } = [];

    public string ActiveRoleId { get; set; } = "pm";

    /// <summary>Last branch typed for bulk checkout: "auto", "current" or a branch name.</summary>
    public string SyncBranch { get; set; } = "auto";

    public bool SyncStash { get; set; }

    public bool SyncKeepCurrentBranch { get; set; }

    /// <summary>Activity window of the PM report, in days.</summary>
    public int ReportDays { get; set; } = 7;

    /// <summary>AI features can be switched off completely.</summary>
    public bool AiEnabled { get; set; } = true;

    public string AiModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Repositories the workspace should contain (name + clone URL), for cloning on a new machine.</summary>
    public List<RepoEntry> RepoList { get; set; } = [];

    /// <summary>Extra regex of hosts the bug hunt reports as hard-coded (staging servers...).</summary>
    public string? BugHuntExtraHostPattern { get; set; }

    /// <summary>True once the settings of the old PowerShell tool were looked at.</summary>
    public bool LegacyImportDone { get; set; }
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>UI dialogs a view model can ask for without knowing the UI framework.</summary>
public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? startFolder = null);

    /// <summary>OK / Cancel question. True when the user confirms.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Save-as dialog. Returns the chosen path, or null when cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension);

    Task CopyToClipboardAsync(string text);

    /// <summary>Open-file dialog. Returns the chosen path, or null when cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title);

    /// <summary>Asks for a secret (masked input). Returns null when cancelled.</summary>
    Task<string?> PromptSecretAsync(string title, string label);

    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    Task<string?> PromptTextAsync(string title, string label, string? initial = null);
}
