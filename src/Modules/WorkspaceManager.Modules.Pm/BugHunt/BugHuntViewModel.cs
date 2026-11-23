using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm.BugHunt;

public sealed record FindingRow(Finding Finding)
{
    public string SeverityText => Finding.Severity.ToString().ToUpperInvariant();

    public Severity Severity => Finding.Severity;

    public string For => Finding.Audience.ToString();

    public string Rule => Finding.Rule;

    public string Repo => Finding.Repo;

    public string Title => Finding.Vi.Title;

    public string What => Finding.What;

    public string Where => Finding.Where;
}

public sealed record DiffRow(string Repo, string Base, int Files, int Commits);

/// <summary>Bug hunt of a branch across the workspace: risks found in the diff, one bilingual PCL case each.</summary>
public sealed partial class BugHuntViewModel : ObservableObject, IPage
{
    private readonly BugHuntService _service;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<BugHuntViewModel> _logger;
    private List<Finding> _all = [];

    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _audienceFilter = "All";

    [ObservableProperty]
    private string _extraHostPattern;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Type a branch (as on origin) and press Run. Uses the last fetch.";

    public BugHuntViewModel(BugHuntService service, ISettingsStore settings, IDialogService dialogs, ILogger<BugHuntViewModel> logger)
    {
        _service = service;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        var s = settings.Load();
        _branch = s.SyncBranch is SyncRequest.Auto or SyncRequest.Current ? string.Empty : s.SyncBranch;
        _extraHostPattern = s.BugHuntExtraHostPattern ?? string.Empty;
    }

    public IReadOnlyList<string> Audiences { get; } = ["All", "Business", "Dev"];

    public ObservableCollection<FindingRow> Findings { get; } = [];

    public ObservableCollection<DiffRow> Diffs { get; } = [];

    public Task OnFirstShownAsync() => Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync()
    {
        var root = _settings.Load().WorkspaceRoot;
        var branch = Branch.Trim();
        if (string.IsNullOrEmpty(root) || branch.Length == 0)
        {
            StatusText = string.IsNullOrEmpty(root) ? "Choose the workspace folder first." : "Type a branch name.";
            return;
        }

        var options = new BugHuntOptions { ExtraHostPattern = ExtraHostPattern };
        IsBusy = true;
        StatusText = $"Scanning origin/{branch}...";
        try
        {
            var (diffs, findings) = await Task.Run(() => _service.RunAsync(root, branch, options));
            Diffs.Clear();
            foreach (var d in diffs)
            {
                Diffs.Add(new DiffRow(d.Repo, d.BaseRef, d.ChangedFiles.Count, d.Commits.Count));
            }

            _all = [.. findings];
            ApplyFilter();
            StatusText = diffs.Count == 0
                ? $"No repository has origin/{branch} (fetch first?)."
                : string.Create(CultureInfo.InvariantCulture,
                    $"{diffs.Count} repositories, {findings.Count} findings: {Count(Severity.Critical)} critical, {Count(Severity.High)} high, {Count(Severity.Medium)} medium, {Count(Severity.Low)} low");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or ArgumentException)
        {
            _logger.LogError(ex, "Bug hunt failed");
            StatusText = "Bug hunt failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand]
    private Task ExportViAsync() => ExportAsync(vietnamese: true);

    [RelayCommand]
    private Task ExportEnAsync() => ExportAsync(vietnamese: false);

    [RelayCommand]
    private async Task CopyViAsync()
    {
        await _dialogs.CopyToClipboardAsync(BugHuntEngine.ToTsv(Visible(), vietnamese: true));
        StatusText = "PCL (VI) copied: paste it into the spreadsheet.";
    }

    partial void OnAudienceFilterChanged(string value) => ApplyFilter();

    partial void OnExtraHostPatternChanged(string value)
    {
        var s = _settings.Load();
        s.BugHuntExtraHostPattern = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.Save(s);
    }

    private async Task ExportAsync(bool vietnamese)
    {
        var name = $"bughunt-{Safe(Branch)}-{(vietnamese ? "VI" : "EN")}.tsv";
        var path = await _dialogs.PickSaveFileAsync("Export the PCL", name, "tsv");
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, BugHuntEngine.ToTsv(Visible(), vietnamese), new System.Text.UTF8Encoding(true));
        StatusText = "Exported to " + path;
    }

    private IEnumerable<Finding> Visible() => Findings.Select(r => r.Finding);

    private void ApplyFilter()
    {
        Findings.Clear();
        foreach (var f in _all.Where(f => AudienceFilter == "All" || f.Audience.ToString() == AudienceFilter))
        {
            Findings.Add(new FindingRow(f));
        }
    }

    private int Count(Severity s) => _all.Count(f => f.Severity == s);

    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-'));
}
