using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Ai;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Platform;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Ai;

/// <summary>AI overview report: PM report data + the user's notes, written up by the AI model in Vietnamese.</summary>
public sealed partial class AiReportViewModel : ObservableObject, IPage
{
    public const string NotesFileName = "ai-notes.txt";

    private readonly IPmReportService _reports;
    private readonly AiReportService _ai;
    private readonly IAiClient _client;
    private readonly IAiKeyStore _keys;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IPlatformService _platform;
    private readonly ILogger<AiReportViewModel> _logger;
    private readonly TimeProvider _time;

    [ObservableProperty]
    private string _model;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _aiEnabled;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _keyStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _result = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string _preview = string.Empty;

    private string? _lastResultFile;

    public AiReportViewModel(
        IPmReportService reports,
        AiReportService ai,
        IAiClient client,
        IAiKeyStore keys,
        ISettingsStore settings,
        IDialogService dialogs,
        IPlatformService platform,
        ILogger<AiReportViewModel> logger,
        TimeProvider? time = null)
    {
        _reports = reports;
        _ai = ai;
        _client = client;
        _keys = keys;
        _settings = settings;
        _dialogs = dialogs;
        _platform = platform;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        var s = settings.Load();
        _model = s.AiModel;
        _aiEnabled = s.AiEnabled;
        UpdateKeyStatus();
    }

    public IReadOnlyList<string> Models { get; } = ["gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.5-flash-lite"];

    public bool HasResult => Result.Length > 0;

    public bool HasPreview => Preview.Length > 0;

    private string NotesPath => Path.Combine(_platform.AppDataDirectory, NotesFileName);

    public Task OnFirstShownAsync()
    {
        Notes = LoadNotes();
        var last = LatestSavedReport();
        if (last is not null)
        {
            Result = File.ReadAllText(last);
            _lastResultFile = last;
            StatusText = "Last report: " + Path.GetFileName(last);
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateAsync()
    {
        if (!_client.IsConfigured)
        {
            StatusText = "No API key: use Set key or Import from .env.";
            return;
        }

        var report = await BuildReportAsync();
        if (report is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = $"Asking {Model}...";
        try
        {
            var notes = Notes;
            var model = Model.Trim();
            Result = await Task.Run(() => _ai.GenerateAsync(report, notes, model));
            _lastResultFile = SaveResult(Result);
            StatusText = string.Create(CultureInfo.InvariantCulture, $"Done with {model} | saved to {_lastResultFile}");
        }
        catch (Exception ex) when (ex is AiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "AI report failed");
            StatusText = "AI report failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGenerate() => AiEnabled && !IsBusy;

    /// <summary>Shows exactly what will be sent to the AI provider.</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    public async Task PreviewAsync()
    {
        var report = await BuildReportAsync();
        if (report is not null)
        {
            Preview = AiReportService.BuildPrompt(report, Notes);
            StatusText = string.Create(CultureInfo.InvariantCulture, $"{Preview.Length:N0} characters would be sent to the AI provider.");
        }
    }

    private bool CanPreview() => !IsBusy;

    [RelayCommand]
    private void ClosePreview() => Preview = string.Empty;

    [RelayCommand]
    private async Task SetKeyAsync()
    {
        var key = await _dialogs.PromptSecretAsync("Gemini API key", "Paste the key. It is stored encrypted for your Windows user only.");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _keys.SaveKey(key);
        UpdateKeyStatus();
        StatusText = "Key saved.";
    }

    [RelayCommand]
    private async Task ImportKeyAsync()
    {
        var file = await _dialogs.PickOpenFileAsync("Pick a .env file that contains GEMINI_API_KEY");
        if (file is null)
        {
            return;
        }

        var key = WorkspaceManager.Ai.AiKeyStore.ReadFromEnvFile(file);
        if (key is null)
        {
            StatusText = "No GEMINI...KEY line with a value in " + Path.GetFileName(file);
            return;
        }

        _keys.SaveKey(key);
        UpdateKeyStatus();
        StatusText = "Key imported from " + Path.GetFileName(file) + " and stored encrypted.";
    }

    [RelayCommand]
    private void RemoveKey()
    {
        _keys.DeleteKey();
        UpdateKeyStatus();
    }

    [RelayCommand]
    private async Task CopyResultAsync()
    {
        await _dialogs.CopyToClipboardAsync(Result);
        StatusText = "Report copied.";
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (!HasResult)
        {
            return;
        }

        var html = Path.Combine(_platform.AppDataDirectory, "reports", "ai-report-latest.html");
        Directory.CreateDirectory(Path.GetDirectoryName(html)!);
        await File.WriteAllTextAsync(html, MarkdownHtml.Page(Result, "Workspace AI Report", $"{Model} | {_time.GetLocalNow():yyyy-MM-dd HH:mm}"));
        try
        {
            _platform.OpenUrl(new Uri(html));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StatusText = "Cannot open the browser: " + ex.Message;
        }
    }

    partial void OnModelChanged(string value) => Save(s => s.AiModel = value.Trim());

    partial void OnAiEnabledChanged(bool value) => Save(s => s.AiEnabled = value);

    partial void OnNotesChanged(string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
            File.WriteAllText(NotesPath, value);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Cannot save the notes");
        }
    }

    private async Task<WorkspaceReport?> BuildReportAsync()
    {
        var s = _settings.Load();
        if (string.IsNullOrEmpty(s.WorkspaceRoot))
        {
            StatusText = "Choose the workspace folder in Repositories first.";
            return null;
        }

        IsBusy = true;
        StatusText = "Collecting workspace data...";
        try
        {
            var focus = s.SyncBranch is SyncRequest.Auto or SyncRequest.Current ? null : s.SyncBranch;
            var options = new ReportOptions(Math.Clamp(s.ReportDays, 1, 90), focus);
            var root = s.WorkspaceRoot;
            return await Task.Run(() => _reports.BuildAsync(root, options));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string LoadNotes()
    {
        if (File.Exists(NotesPath))
        {
            return File.ReadAllText(NotesPath);
        }

        // One-time import from the PowerShell tool: <workspace>\_reports\ai_notes.txt
        var root = _settings.Load().WorkspaceRoot;
        var legacy = root is null ? null : Path.Combine(root, "_reports", "ai_notes.txt");
        return legacy is not null && File.Exists(legacy) ? File.ReadAllText(legacy) : string.Empty;
    }

    private string SaveResult(string markdown)
    {
        var dir = Path.Combine(_platform.AppDataDirectory, "reports");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"ai-report-{_time.GetLocalNow():yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, markdown);
        return file;
    }

    private string? LatestSavedReport()
    {
        var dir = Path.Combine(_platform.AppDataDirectory, "reports");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "ai-report-*.md").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    private void UpdateKeyStatus() =>
        KeyStatus = _keys.KeySource is { } src ? $"Key: from {src}" : "Key: not set";

    private void Save(Action<AppSettings> change)
    {
        var s = _settings.Load();
        change(s);
        _settings.Save(s);
    }
}
