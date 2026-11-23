using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceManager.Core.Ai;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Ai;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Tests;

internal sealed class FakeAi(string answer = "## Tóm tắt\n- ok", Exception? error = null) : IAiClient
{
    public bool IsConfigured { get; set; } = true;

    public List<(string Prompt, string Model)> Calls { get; } = [];

    public Task<string> GenerateAsync(string prompt, string model, CancellationToken cancellationToken = default)
    {
        Calls.Add((prompt, model));
        return error is null ? Task.FromResult(answer) : Task.FromException<string>(error);
    }
}

internal sealed class FakeKeys : IAiKeyStore
{
    public string? Key { get; set; }

    public string? KeySource => Key is null ? null : "secret store";

    public string? GetKey() => Key;

    public void SaveKey(string key) => Key = key;

    public void DeleteKey() => Key = null;
}

internal sealed class StaticReports : IPmReportService
{
    public Task<WorkspaceReport> BuildAsync(string workspaceRoot, ReportOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(PmReportBuilder.Build(workspaceRoot,
            [Insight.Of("api", ahead: 1, commits: [Insight.Commit("api", "aaa111", "Me", "me@example.com", 2, "feat: login")])],
            options, Make.Now));
}

public sealed class AiReportViewModelTests : IDisposable
{
    private readonly string _appData = Directory.CreateTempSubdirectory("wm-ai-vm").FullName;

    private (AiReportViewModel Vm, FakeAi Ai, FakeKeys Keys, FakeDialogs Dialogs, MemorySettings Settings) Create(FakeAi? ai = null, AppSettings? settings = null)
    {
        ai ??= new FakeAi();
        var keys = new FakeKeys { Key = "k" };
        var dialogs = new FakeDialogs(null);
        var store = new MemorySettings(settings ?? new AppSettings { WorkspaceRoot = "/ws" });
        var platform = new AppDataPlatform(_appData);
        var vm = new AiReportViewModel(new StaticReports(), new AiReportService(ai), ai, keys, store, dialogs, platform,
            NullLogger<AiReportViewModel>.Instance, new FixedTime(Make.Now));
        return (vm, ai, keys, dialogs, store);
    }

    [Fact]
    public async Task Generate_sends_report_data_and_notes_and_saves_the_answer()
    {
        var (vm, ai, _, _, _) = Create();
        await vm.OnFirstShownAsync();
        vm.Notes = "Blocked by the staging server";

        await vm.GenerateAsync();

        var (prompt, model) = ai.Calls.Single();
        model.Should().Be("gemini-2.5-flash");
        prompt.Should().Contain("Vietnamese").And.Contain("feat: login").And.Contain("Blocked by the staging server");
        vm.Result.Should().StartWith("## Tóm tắt");
        Directory.GetFiles(Path.Combine(_appData, "reports"), "ai-report-*.md").Should().ContainSingle();
        File.ReadAllText(Path.Combine(_appData, AiReportViewModel.NotesFileName)).Should().Be("Blocked by the staging server");
    }

    [Fact]
    public async Task Preview_shows_the_prompt_without_calling_the_ai()
    {
        var (vm, ai, _, _, _) = Create();

        await vm.PreviewAsync();

        vm.HasPreview.Should().BeTrue();
        vm.Preview.Should().Contain("RAW DATA");
        ai.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_ai_cannot_generate()
    {
        var (vm, _, _, _, settings) = Create();

        vm.AiEnabled = false;

        vm.GenerateCommand.CanExecute(null).Should().BeFalse();
        settings.Current.AiEnabled.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Missing_key_is_reported()
    {
        var ai = new FakeAi { IsConfigured = false };
        var (vm, _, _, _, _) = Create(ai);

        await vm.GenerateAsync();

        ai.Calls.Should().BeEmpty();
        vm.StatusText.Should().Contain("No API key");
    }

    [Fact]
    public async Task Errors_are_shown_not_thrown()
    {
        var (vm, _, _, _, _) = Create(new FakeAi(error: new AiException("Gemini error 400: bad")));

        await vm.GenerateAsync();

        vm.StatusText.Should().Be("AI report failed: Gemini error 400: bad");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Key_can_be_set_or_imported_from_an_env_file()
    {
        var (vm, _, keys, dialogs, _) = Create();
        dialogs.Secret = "typed-key";
        await vm.SetKeyCommand.ExecuteAsync(null);
        keys.Key.Should().Be("typed-key");

        var env = Path.Combine(_appData, "x.env");
        File.WriteAllText(env, "GEMINI_API_KEY=from-file\n");
        dialogs.OpenPath = env;
        await vm.ImportKeyCommand.ExecuteAsync(null);
        keys.Key.Should().Be("from-file");
        vm.KeyStatus.Should().Be("Key: from secret store");
    }

    public void Dispose() => Directory.Delete(_appData, recursive: true);

    private sealed class AppDataPlatform(string dir) : WorkspaceManager.Core.Platform.IPlatformService
    {
        public string AppDataDirectory => dir;

        public void OpenFolder(string path)
        {
        }

        public void OpenTerminal(string workingDirectory)
        {
        }

        public void OpenInEditor(string path)
        {
        }

        public void OpenUrl(Uri url)
        {
        }
    }
}
