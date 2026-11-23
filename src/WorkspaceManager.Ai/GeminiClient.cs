using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Ai;

namespace WorkspaceManager.Ai;

/// <summary>Google Gemini <c>generateContent</c>. The key goes in a header, never in the URL or the logs.</summary>
public sealed class GeminiClient(HttpClient http, IAiKeyStore keys, ILogger<GeminiClient> logger) : IAiClient
{
    public static readonly Uri BaseUri = new("https://generativelanguage.googleapis.com/v1beta/models/");
    private const int MaxAttempts = 3;

    public bool IsConfigured => keys.GetKey() is not null;

    /// <summary>Delay before a retry; tests set it to zero.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(4);

    public async Task<string> GenerateAsync(string prompt, string model, CancellationToken cancellationToken = default)
    {
        var key = keys.GetKey() ?? throw new AiException("No Gemini API key. Set one in AI Report > Set key.");
        if (string.IsNullOrWhiteSpace(model) || model.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '.' or '_')))
        {
            throw new AiException($"Invalid model name '{model}'.");
        }

        var body = new GenerateRequest(
            [new Content("user", [new Part(prompt)])],
            new GenerationConfig(0.3, 16384));

        for (var attempt = 1; ; attempt++)
        {
            // Concatenate: as a relative URI, "gemini-2.5-flash:generateContent" would parse as a scheme.
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri.AbsoluteUri + model + ":generateContent"))
            {
                Content = JsonContent.Create(body, GeminiJson.Default.GenerateRequest),
            };
            request.Headers.Add("x-goog-api-key", key);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Gemini call failed (attempt {Attempt}), retrying", attempt);
                await Task.Delay(RetryDelay * attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return Extract(text);
                }

                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
                if (retryable && attempt < MaxAttempts)
                {
                    logger.LogWarning("Gemini answered {Status} (attempt {Attempt}), retrying", (int)response.StatusCode, attempt);
                    await Task.Delay(RetryDelay * attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new AiException($"Gemini error {(int)response.StatusCode}: {ErrorMessage(text)}".Replace(key, "***", StringComparison.Ordinal));
            }
        }
    }

    private static string Extract(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new AiException("Gemini returned no answer (blocked or empty).");
        }

        var first = candidates[0];
        var parts = first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var p) ? p : default;
        var text = parts.ValueKind == JsonValueKind.Array
            ? string.Concat(parts.EnumerateArray().Select(x => x.TryGetProperty("text", out var t) ? t.GetString() : null))
            : string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            var reason = first.TryGetProperty("finishReason", out var r) ? r.GetString() : "unknown";
            throw new AiException($"Gemini returned an empty answer (finish reason: {reason}).");
        }

        return text.Trim();
    }

    private static string ErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? json;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return json.Length > 300 ? json[..300] : json;
        }
    }

}

internal sealed record GenerateRequest(IReadOnlyList<Content> Contents, GenerationConfig GenerationConfig);

internal sealed record Content(string Role, IReadOnlyList<Part> Parts);

internal sealed record Part(string Text);

internal sealed record GenerationConfig(double Temperature, int MaxOutputTokens);

/// <summary>Source-generated JSON: no reflection, works when trimmed or AOT-compiled.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GenerateRequest))]
internal sealed partial class GeminiJson : JsonSerializerContext;
