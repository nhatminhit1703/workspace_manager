using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Modules.Pm.Repos;

/// <summary>One line of the operation log under the grid.</summary>
public sealed record SyncLogLine(string Time, string Repo, string Status, string Message, string Kind);

/// <summary>Bulk fetch / checkout / pull on the ticked repositories.</summary>
public sealed partial class ReposViewModel
{
    [ObservableProperty]
    private string _syncBranch = SyncRequest.Auto;

    [ObservableProperty]
    private bool _syncStash;

    [ObservableProperty]
    private bool _syncKeepCurrentBranch;

    public ObservableCollection<string> BranchSuggestions { get; } = [SyncRequest.Auto, SyncRequest.Current];

    public ObservableCollection<SyncLogLine> Log { get; } = [];

    /// <summary>True when the branch box holds a real branch name (the "Has branch" column is then meaningful).</summary>
    public bool IsNamedBranch => !string.IsNullOrWhiteSpace(SyncBranch) && SyncBranch.Trim() is not (SyncRequest.Auto or SyncRequest.Current);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public Task FetchAsync() => RunSyncAsync(new SyncRequest(SyncMode.FetchOnly), "Fetch");

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task CheckoutAndPullAsync()
    {
        var branch = string.IsNullOrWhiteSpace(SyncBranch) ? SyncRequest.Auto : SyncBranch.Trim();
        var repos = SelectedRows();
        if (repos.Count == 0)
        {
            StatusText = "Tick at least one repository.";
            return;
        }

        var how = SyncKeepCurrentBranch ? "update the local branch ref only (no switch)" : "switch to it and fast-forward";
        var message = string.Create(CultureInfo.InvariantCulture,
            $"Branch: {branch}\nAction: {how}\nStash local changes: {(SyncStash ? "yes" : "no")}\n\nRepositories ({repos.Count}):\n  {string.Join("\n  ", repos.Select(r => r.Name))}");
        if (!await _dialogs.ConfirmAsync("Checkout + pull", message))
        {
            return;
        }

        await RunSyncAsync(new SyncRequest(SyncMode.CheckoutAndPull, branch, SyncKeepCurrentBranch, SyncStash), $"Checkout + pull '{branch}'");
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public Task PullCurrentAsync() =>
        RunSyncAsync(new SyncRequest(SyncMode.CheckoutAndPull, SyncRequest.Current, StashLocalChanges: SyncStash), "Pull current branch");

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private async Task RunSyncAsync(SyncRequest request, string title)
    {
        var repos = SelectedRows();
        if (repos.Count == 0)
        {
            StatusText = "Tick at least one repository.";
            return;
        }

        IsBusy = true;
        StatusText = $"{title} on {repos.Count} repositories...";
        AddLog(string.Empty, "start", $"{title} -> {string.Join(", ", repos.Select(r => r.Name))}", "info");
        try
        {
            var progress = new OrderedProgress<SyncResult>(r => AddLog(r.Repo, r.Outcome.ToString(), Describe(r), KindOf(r)));
            var results = await _sync.RunAsync(request, repos.Select(r => r.Path).ToList(), progress);
            var ok = results.Count(r => r.IsOk);
            var failed = results.Count(r => r.Outcome == SyncOutcome.Failed);
            var skipped = results.Count(r => r.Outcome == SyncOutcome.Skipped);
            AddLog(string.Empty, "done", string.Create(CultureInfo.InvariantCulture, $"{ok} ok, {skipped} skipped, {failed} failed"), failed > 0 ? "fail" : "info");
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    private List<RepoRowViewModel> SelectedRows() => _all.Where(r => r.IsSelected).ToList();

    private void AddLog(string repo, string status, string message, string kind) =>
        Log.Add(new SyncLogLine(_time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture), repo, status, message, kind));

    private static string Describe(SyncResult r)
    {
        var text = r.Message;
        if (r.Before is not null || r.After is not null)
        {
            text += $" [{r.Before ?? "-"} -> {r.After ?? "-"}]";
        }

        return r.Warning is null ? text : $"{text} | {r.Warning}";
    }

    private static string KindOf(SyncResult r) => r.Outcome switch
    {
        SyncOutcome.Failed => "fail",
        SyncOutcome.Skipped => "skip",
        _ => r.Warning is not null && r.Warning.Contains("conflict", StringComparison.Ordinal) ? "fail" : "ok",
    };

    partial void OnSyncBranchChanged(string value)
    {
        OnPropertyChanged(nameof(IsNamedBranch));
        UpdateTargetBranchColumn();
        SaveSyncOptions();
    }

    partial void OnSyncStashChanged(bool value) => SaveSyncOptions();

    partial void OnSyncKeepCurrentBranchChanged(bool value) => SaveSyncOptions();

    private void UpdateTargetBranchColumn()
    {
        var b = SyncBranch?.Trim() ?? string.Empty;
        foreach (var r in _all)
        {
            r.HasTargetBranch = IsNamedBranch && r.Status.HasRemoteBranch(b);
        }
    }

    private void UpdateBranchSuggestions()
    {
        var names = _all.SelectMany(r => r.Status.RemoteBranches).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
        BranchSuggestions.Clear();
        BranchSuggestions.Add(SyncRequest.Auto);
        BranchSuggestions.Add(SyncRequest.Current);
        foreach (var n in names)
        {
            BranchSuggestions.Add(n);
        }
    }

    private void SaveSyncOptions()
    {
        var s = _settingsStore.Load();
        if (s.SyncBranch == SyncBranch && s.SyncStash == SyncStash && s.SyncKeepCurrentBranch == SyncKeepCurrentBranch)
        {
            return;
        }

        s.SyncBranch = SyncBranch;
        s.SyncStash = SyncStash;
        s.SyncKeepCurrentBranch = SyncKeepCurrentBranch;
        _settingsStore.Save(s);
    }
}

/// <summary>
/// Runs the callback on the caller's synchronization context (the UI thread) and waits for it,
/// so log lines keep their order. <see cref="Progress{T}"/> posts asynchronously and can reorder them.
/// </summary>
internal sealed class OrderedProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Lock _lock = new();

    public void Report(T value)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            lock (_lock)
            {
                handler(value);
            }

            return;
        }

        _context.Send(_ => handler(value), null);
    }
}
