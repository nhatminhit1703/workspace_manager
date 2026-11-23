using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using WorkspaceManager.Core.Modules;

namespace WorkspaceManager.App.Shell;

/// <summary>Main window: navigation built from the modules of the active role, and the current page.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, object> _pages = [];
    private bool _windowShown;
    private IPage? _pendingFirstShow;

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    [ObservableProperty]
    private object? _currentPage;

    public ShellViewModel(IServiceProvider services, Role role, IReadOnlyList<IModule> modules, UpdateBannerViewModel update, NavigatorProxy navigator)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(navigator);
        navigator.Shell = this;
        _services = services;
        Update = update;
        Role = role;
        foreach (var item in modules.SelectMany(m => m.GetNavigationItems())
                     .Where(i => i.RequiredPermission is null || role.Grants(i.RequiredPermission))
                     .OrderBy(i => i.Order))
        {
            Navigation.Add(item);
        }

        SelectedItem = Navigation.FirstOrDefault();
    }

    public Role Role { get; }

    /// <summary>Called once the main window is on screen: pages load their data only then, so the window appears at once.</summary>
    public void OnWindowShown()
    {
        _windowShown = true;
        var p = _pendingFirstShow;
        _pendingFirstShow = null;
        if (p is not null)
        {
            _ = p.OnFirstShownAsync();
        }
    }

    /// <summary>Opens the navigation entry with this id (command line <c>--page</c>).</summary>
    public void Open(string? id)
    {
        var item = Navigation.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SelectedItem = item;
        }
    }

    public UpdateBannerViewModel Update { get; }

    public ObservableCollection<NavigationItem> Navigation { get; } = [];

    public string Title => $"Workspace Manager {Version} - {Role.DisplayName}";

    public static string Version =>
        typeof(ShellViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        if (!_pages.TryGetValue(value.Id, out var page))
        {
            page = _services.GetRequiredService(value.ViewModelType);
            _pages[value.Id] = page;
            if (page is IPage p)
            {
                if (_windowShown)
                {
                    _ = p.OnFirstShownAsync();
                }
                else
                {
                    _pendingFirstShow = p;
                }
            }
        }

        CurrentPage = page;
    }
}
