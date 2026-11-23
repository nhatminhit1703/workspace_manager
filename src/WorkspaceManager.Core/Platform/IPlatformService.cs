namespace WorkspaceManager.Core.Platform;

/// <summary>Everything that differs between operating systems. Windows is implemented first, macOS later.</summary>
public interface IPlatformService
{
    /// <summary>Per-user folder for settings, logs and the local database.</summary>
    string AppDataDirectory { get; }

    void OpenFolder(string path);

    void OpenTerminal(string workingDirectory);

    void OpenInEditor(string path);

    void OpenUrl(Uri url);
}

/// <summary>Secrets (API keys, tokens) encrypted for the current OS user.</summary>
public interface ISecretStore
{
    string? Read(string name);

    void Save(string name, string value);

    void Delete(string name);
}
