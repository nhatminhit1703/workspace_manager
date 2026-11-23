using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Platform;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.Report;

public sealed record TrackingRow(string Repo, string OnOrigin, string Local, string CheckedOut, string Sync, bool IsCheckedOut);

public sealed record AuthorRow(string Author, int Commits, string Repos, string LastCommit);

public sealed record ActionRow(ActionLevel Level, string Repo, string Issue, string SuggestedAction)
{
    public string LevelText => Level.ToString().ToUpperInvariant();
}

/// <summary>PM report page: KPIs, prioritized action items, branch tracking, my work, team activity.</summary>
public sealed partial class PmReportViewModel : ObservableObject, IPage
{
    private readonly IPmReportService _service;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IPlatformService _platform;
    private readonly ILogger<PmReportViewModel> _logger;
    private WorkspaceReport? _report;

    [ObservableProperty]
    private int _days;

    [ObservableProperty]
    private string _focusBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleActionsCount))]
    private bool _showLowAndInfo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _dailyReport = string.Empty;

    [ObservableProperty]
    private bool _hasReport;

    public PmReportViewModel(IPmReportService service, ISettingsStore settings, IDialogService dialogs, IPlatformService platform, ILogger<PmReportViewModel> logger)
    {
        _service = service;
        _settings = settings;
        _dialogs = dialogs;
        _platform = platform;
        _logger = logger;
        var s = settings.Load();
        _days = Math.Clamp(s.ReportDays, 1, 90);
        _focusBranch = s.SyncBranch is SyncRequest.Auto or SyncRequest.Current ? string.Empty : s.SyncBranch;
    }

    public ObservableCollection<ReportKpi> Kpis { get; } = [];

    public ObservableCollection<ActionRow> Actions { get; } = [];

    public ObservableCollection<TrackingRow> Tracking { get; } = [];

    public ObservableCollection<AuthorRow> Team { get; } = [];

    public int VisibleActionsCount => Actions.Count;

    public bool HasTracking => Tracking.Count > 0;

    public Task OnFirstShownAsync() => GenerateAsync();

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    public async Task GenerateAsync()
    {
        var root = _settings.Load().WorkspaceRoot;
        if (string.IsNullOrEmpty(root))
        {
            StatusText = "Choose the workspace folder in Repositories first.";
            return;
        }

        IsBusy = true;
        StatusText = "Building the report...";
        try
        {
            var s = _settings.Load();
            s.ReportDays = Days;
            _settings.Save(s);
            var options = new ReportOptions(Days, string.IsNullOrWhiteSpace(FocusBranch) ? null : FocusBranch.Trim());
            _report = await Task.Run(() => _service.BuildAsync(root, options));
            Show(_report);
            StatusText = string.Create(CultureInfo.InvariantCulture,
                $"Generated {_report.GeneratedAt:HH:mm} from the last fetch | {_report.Repos.Count} repositories | {_report.Commits.Count} commits in {Days} day(s)");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "PM report failed");
            StatusText = "Report failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGenerate() => !IsBusy;

    [RelayCommand]
    private async Task CopyDailyReportAsync()
    {
        await _dialogs.CopyToClipboardAsync(DailyReport);
        StatusText = "Daily report copied to the clipboard.";
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (_report is null)
        {
            return;
        }

        var path = await _dialogs.PickSaveFileAsync("Export the PM report", $"pm-report-{_report.GeneratedAt:yyyyMMdd-HHmm}.html", "html");
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, ReportText.Html(_report));
        StatusText = "Exported to " + path;
        try
        {
            _platform.OpenUrl(new Uri(path));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "Cannot open {Path}", path);
        }
    }

    partial void OnShowLowAndInfoChanged(bool value)
    {
        if (_report is not null)
        {
            FillActions(_report);
        }
    }

    private void Show(WorkspaceReport report)
    {
        Kpis.Clear();
        foreach (var k in report.Kpis)
        {
            Kpis.Add(k);
        }

        FillActions(report);

        Tracking.Clear();
        foreach (var r in report.Repos.Where(r => r.Tracking is { } t && (t.OnRemote || t.OnLocal)))
        {
            var t = r.Tracking!;
            var sync = t.Ahead is null ? string.Empty
                : t.Ahead == 0 && t.Behind == 0 ? "up to date"
                : string.Create(CultureInfo.InvariantCulture, $"behind {t.Behind}, ahead {t.Ahead}");
            Tracking.Add(new TrackingRow(r.Status.Name, t.OnRemote ? "yes" : "no", t.OnLocal ? "yes" : "no",
                t.IsCurrent ? "yes" : $"no (on {r.Status.Branch.Head ?? "detached"})", sync, t.IsCurrent));
        }

        OnPropertyChanged(nameof(HasTracking));

        DailyReport = ReportText.DailyReport(report);

        Team.Clear();
        foreach (var g in report.Commits.GroupBy(c => c.Author).OrderByDescending(g => g.Count()))
        {
            Team.Add(new AuthorRow(g.Key, g.Count(), string.Join(", ", g.Select(c => c.Repo).Distinct().Order(StringComparer.OrdinalIgnoreCase)),
                g.Max(c => c.Date).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }

        HasReport = true;
    }

    private void FillActions(WorkspaceReport report)
    {
        Actions.Clear();
        foreach (var a in report.Actions.Where(a => ShowLowAndInfo || a.Level is ActionLevel.High or ActionLevel.Medium))
        {
            Actions.Add(new ActionRow(a.Level, a.Repo, a.Issue, a.SuggestedAction));
        }

        OnPropertyChanged(nameof(VisibleActionsCount));
    }
}
