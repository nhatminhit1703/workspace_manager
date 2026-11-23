using System.Text.Json;
using WorkspaceManager.Core.Settings;

namespace WorkspaceManager.Data;

/// <summary>
/// One-time import from the old PowerShell tool: its state file (<c>%LOCALAPPDATA%\workspace_manager.json</c>)
/// holds the ticked repositories. The workspace root is not in that file, so it is not imported.
/// </summary>
public static class LegacySettingsImporter
{
    public static string DefaultLegacyStateFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "workspace_manager.json");

    /// <returns>True when something was imported.</returns>
    public static bool Import(AppSettings settings, string legacyStateFile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.LegacyImportDone)
        {
            return false;
        }

        settings.LegacyImportDone = true;
        if (!File.Exists(legacyStateFile))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacyStateFile));
            if (settings.SelectedRepos.Count == 0
                && doc.RootElement.TryGetProperty("Repos", out var repos)
                && repos.ValueKind == JsonValueKind.Array)
            {
                settings.SelectedRepos = repos.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToList();
                return settings.SelectedRepos.Count > 0;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}
