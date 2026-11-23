using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Settings;

namespace WorkspaceManager.Data;

/// <summary>Settings as an indented JSON file. A broken file is kept aside and defaults are used.</summary>
public sealed class JsonSettingsStore(string filePath, ILogger<JsonSettingsStore>? logger = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;
    private readonly Lock _lock = new();

    public string FilePath => filePath;

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(filePath))
            {
                return new AppSettings();
            }

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Json) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                var aside = filePath + ".broken";
                File.Copy(filePath, aside, overwrite: true);
                _logger.LogWarning(ex, "Settings file is not valid JSON, kept as {Aside}; using defaults", aside);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tmp = filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
            File.Move(tmp, filePath, overwrite: true);
        }
    }
}
