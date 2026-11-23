using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Platform;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;

namespace WorkspaceManager.Modules.Pm.Repos;

/// <summary>Repository grid of the workspace (replaces the grid of the PowerShell tool).</summary>
public sealed partial class ReposViewModel : ObservableObject, IPage
{
    private const int MaxParallelReads = 8;

    private readonly IWorkspaceScanner _scanner;
    private readonly IRepoStatusReader _reader;
    private readonly ISyncService _sync;
    private readonly ISettingsStore _settingsStore;
    private readonly IDialogService _dialogs;
    private readonly IPlatformService _platform;
    private readonly ILogger<ReposViewModel> _logger;
    private readonly TimeProvider _time;
    private List<RepoRowViewModel> _all = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private string? _workspaceRoot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutAndPullCommand))]
    [NotifyCanExecuteChangedFor(nameof(PullCurrentCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private RepoRowViewModel? _selectedRepo;

    public ReposViewModel(
        IWorkspaceScanner scanner,
        IRepoStatusReader reader,
        ISyncService sync,
        ISettingsStore settingsStore,
        IDialogService dialogs,
        IPlatformService platform,
        ILogger<ReposViewModel> logger,
        TimeProvider? time = null)
    {
        _scanner = scanner;
        _reader = reader;
        _sync = sync;
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _platform = platform;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        var settings = settingsStore.Load();
        WorkspaceRoot = settings.WorkspaceRoot;
        SyncBranch = settings.SyncBranch;
        SyncStash = settings.SyncStash;
        SyncKeepCurrentBranch = settings.SyncKeepCurrentBranch;
    }

    public ObservableCollection<RepoRowViewModel> Repos { get; } = [];

    public bool HasWorkspace => !string.IsNullOrEmpty(WorkspaceRoot);

    public Task OnFirstShownAsync() => HasWorkspace ? RefreshAsync() : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (!HasWorkspace)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Reading repositories...";
        var keep = new HashSet<string>(
            _all.Count > 0 ? _all.Where(r => r.IsSelected).Select(r => r.Name) : _settingsStore.Load().SelectedRepos,
            StringComparer.OrdinalIgnoreCase);
        try
        {
            var paths = _scanner.FindRepositories(WorkspaceRoot!);
            using var gate = new SemaphoreSlim(MaxParallelReads);
            var statuses = await Task.WhenAll(paths.Select(async p =>
            {
                await gate.WaitAsync();
                try
                {
                    return await _reader.ReadAsync(p);
                }
                finally
                {
                    gate.Release();
                }
            }));

            var now = _time.GetLocalNow();
            _all = statuses.Select(s => new RepoRowViewModel(s, now) { IsSelected = keep.Contains(s.Name) }).ToList();
            UpdateBranchSuggestions();
            UpdateTargetBranchColumn();
            ApplyFilter();
            StatusText = Summary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Reading the workspace {Root} failed", WorkspaceRoot);
            StatusText = "Cannot read the workspace: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand]
    public async Task ChooseWorkspaceAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Choose the workspace folder (the folder that contains your repositories)", WorkspaceRoot);
        if (folder is null)
        {
            return;
        }

        WorkspaceRoot = folder;
        var s = _settingsStore.Load();
        s.WorkspaceRoot = folder;
        _settingsStore.Save(s);
        _all = [];
        await RefreshAsync();
    }

    [RelayCommand]
    public void SelectAll(string? which)
    {
        foreach (var r in _all)
        {
            r.IsSelected = which switch
            {
                "none" => false,
                "changes" => r.HasChanges,
                "behind" => r.IsBehind,
                "has-branch" => r.HasTargetBranch,
                _ => true,
            };
        }

        SaveSelection();
    }

    [RelayCommand]
    private void OpenFolder(RepoRowViewModel? row) => Safe(row, r => _platform.OpenFolder(r.Path));

    [RelayCommand]
    private void OpenTerminal(RepoRowViewModel? row) => Safe(row, r => _platform.OpenTerminal(r.Path));

    [RelayCommand]
    private void OpenInEditor(RepoRowViewModel? row) => Safe(row, r => _platform.OpenInEditor(r.Path));

    public void SaveSelection()
    {
        var s = _settingsStore.Load();
        s.SelectedRepos = _all.Where(r => r.IsSelected).Select(r => r.Name).ToList();
        _settingsStore.Save(s);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        Repos.Clear();
        foreach (var r in _all)
        {
            if (f.Length == 0
                || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || r.Branch.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                Repos.Add(r);
            }
        }
    }

    private string Summary()
    {
        var dirty = _all.Count(r => r.HasChanges);
        var behind = _all.Count(r => r.IsBehind);
        var errors = _all.Count(r => r.HasError);
        var text = string.Create(CultureInfo.InvariantCulture, $"{_all.Count} repositories | {dirty} with uncommitted changes | {behind} behind upstream");
        return errors > 0 ? text + string.Create(CultureInfo.InvariantCulture, $" | {errors} with errors") : text;
    }

    private void Safe(RepoRowViewModel? row, Action<RepoRowViewModel> action)
    {
        var r = row ?? SelectedRepo;
        if (r is null)
        {
            return;
        }

        try
        {
            action(r);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "Cannot open {Path}", r.Path);
            StatusText = "Cannot open: " + ex.Message;
        }
    }
}
