using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Ai;
using WorkspaceManager.Core.Platform;

namespace WorkspaceManager.Ai.Tests;

internal sealed class FakeHandler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
{
    private int _next;

    public List<(Uri Uri, string? Key, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null, body));
        var (status, text) = answers[Math.Min(_next++, answers.Length - 1)];
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}

internal sealed class MemorySecrets : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? Read(string name) => _values.GetValueOrDefault(name);

    public void Save(string name, string value) => _values[name] = value;

    public void Delete(string name) => _values.Remove(name);
}

public class GeminiClientTests
{
    private const string Key = "AIza-test-key-123";
    private const string Ok = """{"candidates":[{"content":{"parts":[{"text":"## Tóm tắt\n- ok"}]},"finishReason":"STOP"}]}""";

    private static (GeminiClient Client, FakeHandler Handler) Create(params (HttpStatusCode, string)[] answers)
    {
        var secrets = new MemorySecrets();
        secrets.Save(AiKeyStore.SecretName, Key);
        var handler = new FakeHandler(answers);
        var client = new GeminiClient(new HttpClient(handler), new AiKeyStore(secrets), NullLogger<GeminiClient>.Instance) { RetryDelay = TimeSpan.Zero };
        return (client, handler);
    }

    [Fact]
    public async Task Sends_the_prompt_with_the_key_in_a_header_and_returns_the_text()
    {
        var (client, handler) = Create((HttpStatusCode.OK, Ok));

        var text = await client.GenerateAsync("Xin chào", "gemini-2.5-flash", TestContext.Current.CancellationToken);

        text.Should().Be("## Tóm tắt\n- ok");
        var req = handler.Requests.Single();
        req.Uri.AbsoluteUri.Should().Be("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        req.Uri.Query.Should().BeEmpty("the key must never be in the URL");
        req.Key.Should().Be(Key);
        using var doc = JsonDocument.Parse(req.Body);
        doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("Xin chào");
        doc.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32().Should().Be(16384);
    }

    [Fact]
    public async Task Retries_on_429_and_server_errors()
    {
        var (client, handler) = Create((HttpStatusCode.TooManyRequests, "{}"), (HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.OK, Ok));

        await client.GenerateAsync("x", "gemini-2.5-flash", TestContext.Current.CancellationToken);

        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Client_errors_fail_at_once_with_the_api_message_and_no_key()
    {
        var (client, handler) = Create((HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key " + Key + " not valid\"}}"));

        var act = () => client.GenerateAsync("x", "gemini-2.5-flash", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiException>()).Which.Message.Should().Contain("400").And.Contain("not valid").And.NotContain(Key);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_answer_is_an_error()
    {
        var (client, _) = Create((HttpStatusCode.OK, """{"candidates":[{"finishReason":"SAFETY"}]}"""));

        var act = () => client.GenerateAsync("x", "gemini-2.5-flash", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiException>()).Which.Message.Should().Contain("SAFETY");
    }

    [Fact]
    public async Task Rejects_a_model_name_that_could_change_the_url()
    {
        var (client, _) = Create((HttpStatusCode.OK, Ok));

        var act = () => client.GenerateAsync("x", "../other?x=1", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AiException>();
    }
}

public sealed class AiKeyStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wm-ai").FullName;

    [Theory]
    [InlineData("GEMINI_API_KEY=abc123", "abc123")]
    [InlineData("export GEMINI_API_KEY=\"abc123\"", "abc123")]
    [InlineData("GEMINI_KEY='abc123' # comment", "abc123")]
    [InlineData("# GEMINI_API_KEY=commented\nGEMINI_MODEL=x\nGEMINI_API_KEY=", null)]
    [InlineData("OTHER_KEY=zzz", null)]
    public void Reads_the_key_from_an_env_file(string content, string? expected)
    {
        var file = Path.Combine(_dir, ".env");
        File.WriteAllText(file, content);

        AiKeyStore.ReadFromEnvFile(file).Should().Be(expected);
    }

    [Fact]
    public void Secret_store_wins_and_reports_its_source()
    {
        var secrets = new MemorySecrets();
        var store = new AiKeyStore(secrets);
        store.SaveKey("  k1  ");

        store.GetKey().Should().Be("k1");
        store.KeySource.Should().Be("secret store");

        store.DeleteKey();
        secrets.Read(AiKeyStore.SecretName).Should().BeNull();
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
