namespace WorkspaceManager.Core.Ai;

/// <summary>A text-generation model. Features depend on this, never on a vendor.</summary>
public interface IAiClient
{
    /// <summary>True when an API key is available.</summary>
    bool IsConfigured { get; }

    Task<string> GenerateAsync(string prompt, string model, CancellationToken cancellationToken = default);
}

/// <summary>The API key of the AI provider, kept in the OS secret store.</summary>
public interface IAiKeyStore
{
    /// <summary>The key, or null. Order: secret store, then the GEMINI_API_KEY environment variable.</summary>
    string? GetKey();

    /// <summary>Where the key comes from ("secret store", "environment"), or null when there is none.</summary>
    string? KeySource { get; }

    void SaveKey(string key);

    void DeleteKey();
}

public sealed class AiException(string message, Exception? inner = null) : Exception(message, inner);
