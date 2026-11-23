using WorkspaceManager.Core.Ai;
using WorkspaceManager.Core.Platform;

namespace WorkspaceManager.Ai;

/// <summary>Gemini key: OS secret store first, then the GEMINI_API_KEY environment variable.</summary>
public sealed class AiKeyStore(ISecretStore? secrets) : IAiKeyStore
{
    public const string SecretName = "gemini-api-key";
    public const string EnvironmentVariable = "GEMINI_API_KEY";

    public string? KeySource =>
        Stored() is not null ? "secret store"
        : FromEnvironment() is not null ? "environment variable " + EnvironmentVariable
        : null;

    public string? GetKey() => Stored() ?? FromEnvironment();

    public void SaveKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        (secrets ?? throw new PlatformNotSupportedException("No secret store on this platform yet.")).Save(SecretName, key.Trim());
    }

    public void DeleteKey() => secrets?.Delete(SecretName);

    /// <summary>
    /// Reads a key from a .env style file (<c>GEMINI_API_KEY=...</c> or any <c>GEMINI*KEY=</c> line).
    /// The user picks the file; nothing workspace-specific is hard-coded.
    /// </summary>
    public static string? ReadFromEnvFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (line.StartsWith('#') || eq <= 0)
            {
                continue;
            }

            var name = line[..eq].Trim();
            if (name.StartsWith("export ", StringComparison.Ordinal))
            {
                name = name[7..].Trim();
            }

            if (!name.StartsWith("GEMINI", StringComparison.OrdinalIgnoreCase) || !name.EndsWith("KEY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Drop a trailing comment first, then the quotes: KEY='abc' # note
            var value = line[(eq + 1)..].Trim();
            var hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0)
            {
                value = value[..hash].Trim();
            }

            value = value.Trim('"', '\'');

            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    private string? Stored() => secrets?.Read(SecretName) is { Length: > 0 } k ? k : null;

    private static string? FromEnvironment() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } k ? k.Trim() : null;
}
