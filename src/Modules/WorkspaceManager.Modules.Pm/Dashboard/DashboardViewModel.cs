using System.Collections.ObjectModel;
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
using WorkspaceManager.Modules.Pm.Report;
using WorkspaceManager.Modules.Pm.Repos;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Dashboard;

/// <summary>One line of the setup checklist.</summary>
public sealed record SetupItem(string Text, bool Done)
{
    public string Mark => Done ? "OK" : "TODO";
}

/// <summary>Home page of the PM role: setup checklist, KPIs, top action items, my work, last AI report, shortcuts.</summary>
public sealed partial class DashboardViewModel : ObservableObject, IPage
{
    private const int TopActions = 8;

    private readonly IPmReportService _reports;
    private readonly ISetupService _setup;
    private readonly ISyncService _sync;
    private readonly IWorkspaceScanner _scanner;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IAiKeyStore _keys;
    private readonly INavigator _navigator;
    private readonly IPlatformService _platform;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly TimeProvider _time;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloneMissingCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _workspaceRoot;

    [ObservableProperty]
    private bool _needsSetup;

    [ObservableProperty]
    private string _repoListText = string.Empty;

    [ObservableProperty]
    private string _myWork = string.Empty;

    [ObservableProperty]
    private string _lastAiReport = string.Empty;

    [ObservableProperty]
    private bool _hasReport;

    public DashboardViewModel(
        IPmReportService reports,
        ISetupService setup,
        ISyncService sync,
        IWorkspaceScanner scanner,
        ISettingsStore settings,
        IDialogService dialogs,
        IAiKeyStore keys,
        INavigator navigator,
        IPlatformService platform,
        ILogger<DashboardViewModel> logger,
        TimeProvider? time = null)
    {
        _reports = reports;
        _setup = setup;
        _sync = sync;
        _scanner = scanner;
        _settings = settings;
        _dialogs = dialogs;
        _keys = keys;
        _navigator = navigator;
        _platform = platform;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        var s = settings.Load();
        _workspaceRoot = s.WorkspaceRoot;
        _repoListText = RepoEntry.Format(s.RepoList);
    }

    public ObservableCollection<SetupItem> Setup { get; } = [];

    public ObservableCollection<ReportKpi> Kpis { get; } = [];

    public ObservableCollection<ActionRow> Actions { get; } = [];

    public ObservableCollection<SyncLogLine> Log { get; } = [];

    public Task OnFirstShownAsync() => RefreshAsync();

    [RelayCommand(CanExecute = nameof(NotBusy))]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            await CheckSetupAsync();
            LastAiReport = ReadLastAiReport();
            if (string.IsNullOrEmpty(WorkspaceRoot))
            {
                StatusText = "Choose the workspace folder to start.";
                return;
            }

            StatusText = "Reading the workspace...";
            var s = _settings.Load();
            var focus = s.SyncBranch is SyncRequest.Auto or SyncRequest.Current ? null : s.SyncBranch;
            var root = WorkspaceRoot;
            var report = await Task.Run(() => _reports.BuildAsync(root, new ReportOptions(2, focus)));
            Show(report);
            StatusText = string.Create(CultureInfo.InvariantCulture, $"Updated {report.GeneratedAt:HH:mm} from the last fetch (activity: today and yesterday).");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Dashboard refresh failed");
            StatusText = "Refresh failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand]
    private async Task ChooseWorkspaceAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Choose the workspace folder (the folder that contains your repositories)", WorkspaceRoot);
        if (folder is null)
        {
            return;
        }

        var s = _settings.Load();
        s.WorkspaceRoot = folder;
        _settings.Save(s);
        WorkspaceRoot = folder;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SetIdentityAsync()
    {
        var current = await _setup.GetIdentityAsync();
        var name = await _dialogs.PromptTextAsync("Git identity", "Your name for commits (git user.name):", current.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var email = await _dialogs.PromptTextAsync("Git identity", "Your e-mail for commits (git user.email):", current.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        await _setup.SetIdentityAsync(new GitIdentity(name.Trim(), email.Trim()));
        await CheckSetupAsync();
        StatusText = "Git identity saved in the global git configuration.";
    }

    [RelayCommand]
    private async Task ImportGeminiKeyAsync()
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
        await CheckSetupAsync();
        StatusText = "Gemini key imported and stored encrypted.";
    }

    [RelayCommand]
    private async Task SaveRepoListAsync()
    {
        var s = _settings.Load();
        s.RepoList = RepoEntry.ParseList(RepoListText);
        _settings.Save(s);
        RepoListText = RepoEntry.Format(s.RepoList);
        await CheckSetupAsync();
        StatusText = string.Create(CultureInfo.InvariantCulture, $"{s.RepoList.Count} repositories in the list (saved on this machine only).");
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task CloneMissingAsync()
    {
        if (string.IsNullOrEmpty(WorkspaceRoot))
        {
            StatusText = "Choose the workspace folder first.";
            return;
        }

        var missing = MissingRepos();
        if (missing.Count == 0)
        {
            StatusText = "Every repository of the list is already there.";
            return;
        }

        if (!await _dialogs.ConfirmAsync("Clone repositories", $"Clone {missing.Count} repositories into {WorkspaceRoot}?\n\n  {string.Join("\n  ", missing.Select(m => m.Name))}"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new OrderedProgress<SyncResult>(r => AddLog(r));
            var root = WorkspaceRoot;
            await Task.Run(() => _setup.CloneMissingAsync(root, missing, progress));
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task FetchAllAsync()
    {
        if (string.IsNullOrEmpty(WorkspaceRoot))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Fetching every repository...";
        try
        {
            var root = WorkspaceRoot;
            var progress = new OrderedProgress<SyncResult>(r => AddLog(r));
            var results = await Task.Run(() => _sync.RunAsync(new SyncRequest(SyncMode.FetchOnly), _scanner.FindRepositories(root), progress));
            StatusText = string.Create(CultureInfo.InvariantCulture, $"Fetched {results.Count(r => r.IsOk)} of {results.Count} repositories.");
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void Open(string? navigationId)
    {
        if (navigationId is not null)
        {
            _navigator.Navigate(navigationId);
        }
    }

    private async Task CheckSetupAsync()
    {
        var git = await Task.Run(() => _setup.GitVersionAsync());
        var identity = git is null ? new GitIdentity(null, null) : await Task.Run(() => _setup.GetIdentityAsync());
        var missing = MissingRepos();
        var list = _settings.Load().RepoList;

        Setup.Clear();
        Setup.Add(new SetupItem(git ?? "Git is not installed or not on the PATH (https://git-scm.com/downloads)", git is not null));
        Setup.Add(new SetupItem(string.IsNullOrEmpty(WorkspaceRoot) ? "Workspace folder: not chosen" : "Workspace folder: " + WorkspaceRoot, !string.IsNullOrEmpty(WorkspaceRoot)));
        Setup.Add(new SetupItem(identity.IsComplete ? $"Git identity: {identity.Name} <{identity.Email}>" : "Git identity (user.name / user.email): not set", identity.IsComplete));
        Setup.Add(new SetupItem(
            list.Count == 0 ? "Repository list: empty (optional, used to clone on a new machine)"
            : missing.Count == 0 ? string.Create(CultureInfo.InvariantCulture, $"Repository list: all {list.Count} cloned")
            : string.Create(CultureInfo.InvariantCulture, $"Repository list: {missing.Count} of {list.Count} not cloned yet"),
            missing.Count == 0));
        Setup.Add(new SetupItem(_keys.KeySource is { } src ? "AI key: from " + src : "AI key: not set (optional, for the AI report)", _keys.KeySource is not null));
        NeedsSetup = Setup.Take(4).Any(i => !i.Done);
    }

    private List<RepoEntry> MissingRepos() =>
        string.IsNullOrEmpty(WorkspaceRoot)
            ? []
            : _settings.Load().RepoList.Where(e => !Directory.Exists(Path.Combine(WorkspaceRoot, e.Name))).ToList();

    private void Show(WorkspaceReport report)
    {
        Kpis.Clear();
        foreach (var k in report.Kpis.Where(k => k.Name is not ("Repos" or "Stashes")))
        {
            Kpis.Add(k);
        }

        Actions.Clear();
        foreach (var a in report.Actions.Where(a => a.Level is ActionLevel.High or ActionLevel.Medium).Take(TopActions))
        {
            Actions.Add(new ActionRow(a.Level, a.Repo, a.Issue, a.SuggestedAction));
        }

        MyWork = ReportText.DailyReport(report);
        HasReport = true;
    }

    private string ReadLastAiReport()
    {
        var dir = Path.Combine(_platform.AppDataDirectory, "reports");
        var last = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "ai-report-*.md").OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (last is null)
        {
            return "No AI report yet. Open AI Report and press Generate.";
        }

        var lines = File.ReadLines(last).Take(18).ToList();
        return $"({Path.GetFileNameWithoutExtension(last)})\n" + string.Join('\n', lines);
    }

    private void AddLog(SyncResult r) =>
        Log.Add(new SyncLogLine(_time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture), r.Repo, r.Outcome.ToString(), r.Message,
            r.Outcome switch { SyncOutcome.Failed => "fail", SyncOutcome.Skipped => "skip", _ => "ok" }));
}
