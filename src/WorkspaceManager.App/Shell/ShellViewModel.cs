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

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    [ObservableProperty]
    private object? _currentPage;

    public ShellViewModel(IServiceProvider services, Role role, IReadOnlyList<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(role);
        _services = services;
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
                _ = p.OnFirstShownAsync();
            }
        }

        CurrentPage = page;
    }
}
