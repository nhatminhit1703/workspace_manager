namespace WorkspaceManager.Core.Settings;

/// <summary>Per-user settings, stored outside the repository (see IPlatformService.AppDataDirectory).</summary>
public sealed class AppSettings
{
    /// <summary>Folder whose direct sub folders are the repositories of the workspace.</summary>
    public string? WorkspaceRoot { get; set; }

    /// <summary>Repositories ticked in the grid (bulk operations work on them).</summary>
    public List<string> SelectedRepos { get; set; } = [];

    public string ActiveRoleId { get; set; } = "pm";

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
}
