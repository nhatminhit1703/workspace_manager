using WorkspaceManager.Core.Settings;
using WorkspaceManager.Data;

namespace WorkspaceManager.Modules.Pm.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wm-tests", Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Settings_round_trip()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "sub", "settings.json"));
        store.Save(new AppSettings { WorkspaceRoot = @"C:\ws", SelectedRepos = ["a", "b"] });

        var loaded = store.Load();

        loaded.WorkspaceRoot.Should().Be(@"C:\ws");
        loaded.SelectedRepos.Should().Equal("a", "b");
    }

    [Fact]
    public void Broken_settings_file_is_kept_aside_and_defaults_are_used()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ not json");

        var loaded = new JsonSettingsStore(file).Load();

        loaded.WorkspaceRoot.Should().BeNull();
        File.Exists(file + ".broken").Should().BeTrue();
    }

    [Fact]
    public void Legacy_PowerShell_selection_is_imported_once()
    {
        var legacy = Path.Combine(_dir, "workspace_manager.json");
        File.WriteAllText(legacy, """{ "Branch": "auto", "Repos": ["api", "shipping"], "Days": 7 }""");
        var s = new AppSettings();

        LegacySettingsImporter.Import(s, legacy).Should().BeTrue();
        s.SelectedRepos.Should().Equal("api", "shipping");
        s.LegacyImportDone.Should().BeTrue();

        s.SelectedRepos = [];
        LegacySettingsImporter.Import(s, legacy).Should().BeFalse("the import runs only once");
        s.SelectedRepos.Should().BeEmpty();
    }

    [Fact]
    public void Missing_legacy_file_is_not_an_error()
    {
        var s = new AppSettings();

        LegacySettingsImporter.Import(s, Path.Combine(_dir, "none.json")).Should().BeFalse();
        s.LegacyImportDone.Should().BeTrue();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
