using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using WorkspaceManager.Core.Platform;

namespace WorkspaceManager.Platform;

public static class PlatformServices
{
    public const string AppFolderName = "WorkspaceManager";

    public static IPlatformService CreatePlatformService() =>
        OperatingSystem.IsWindows() ? new WindowsPlatformService() : new GenericPlatformService();

    /// <summary>Secret store of the current OS. Only Windows (DPAPI) for now; macOS Keychain comes in Phase 7.</summary>
    public static ISecretStore CreateSecretStore(string appDataDirectory) =>
        OperatingSystem.IsWindows()
            ? new DpapiSecretStore(Path.Combine(appDataDirectory, "secrets"))
            : throw new PlatformNotSupportedException("Secret storage is only implemented on Windows for now.");

    internal static void Start(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = args.Length == 0 };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var _ = Process.Start(psi);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformService : IPlatformService
{
    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), PlatformServices.AppFolderName);

    public void OpenFolder(string path) => PlatformServices.Start("explorer.exe", path);

    public void OpenTerminal(string workingDirectory)
    {
        // Prefer Git Bash (installed with Git for Windows), then Windows Terminal, then cmd.
        var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "git-bash.exe");
        if (File.Exists(gitBash))
        {
            PlatformServices.Start(gitBash, $"--cd={workingDirectory}");
            return;
        }

        try
        {
            PlatformServices.Start("wt.exe", "-d", workingDirectory);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            var psi = new ProcessStartInfo("cmd.exe") { WorkingDirectory = workingDirectory, UseShellExecute = true };
            using var _ = Process.Start(psi);
        }
    }

    public void OpenInEditor(string path)
    {
        // "code" is a .cmd shim on Windows: start it through cmd so PATHEXT is honoured, without a window.
        var psi = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("code");
        psi.ArgumentList.Add(path);
        using var _ = Process.Start(psi);
    }

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        PlatformServices.Start(url.AbsoluteUri);
    }
}

/// <summary>Fallback for macOS / Linux until Phase 7 (uses "open" / "xdg-open").</summary>
public sealed class GenericPlatformService : IPlatformService
{
    private static string Opener => OperatingSystem.IsMacOS() ? "open" : "xdg-open";

    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), PlatformServices.AppFolderName);

    public void OpenFolder(string path) => PlatformServices.Start(Opener, path);

    public void OpenTerminal(string workingDirectory) =>
        PlatformServices.Start(OperatingSystem.IsMacOS() ? "open" : "x-terminal-emulator", OperatingSystem.IsMacOS() ? ["-a", "Terminal", workingDirectory] : []);

    public void OpenInEditor(string path) => PlatformServices.Start("code", path);

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        PlatformServices.Start(Opener, url.AbsoluteUri);
    }
}

/// <summary>Secrets encrypted with DPAPI for the current Windows user, one file per secret.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    public string? Read(string name)
    {
        var file = FileOf(name);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Save(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(directory);
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FileOf(name), blob);
    }

    public void Delete(string name)
    {
        var file = FileOf(name);
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Reads a key saved by the old PowerShell tool (ConvertFrom-SecureString: hex of a DPAPI blob of UTF-16 text).
    /// </summary>
    public static string? ReadLegacyPowerShellSecret(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var blob = Convert.FromHexString(File.ReadAllText(file).Trim());
            return Encoding.Unicode.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private string FileOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid secret name.", nameof(name));
        }

        return Path.Combine(directory, name + ".bin");
    }
}
