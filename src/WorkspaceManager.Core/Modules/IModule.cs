using Microsoft.Extensions.DependencyInjection;

namespace WorkspaceManager.Core.Modules;

/// <summary>
/// A group of features (screens, commands, background jobs). Roles are lists of modules,
/// so a new role is a new module plus a role definition, without changes to other modules.
/// </summary>
public interface IModule
{
    /// <summary>Stable id, used in role definitions and settings.</summary>
    string Id { get; }

    string DisplayName { get; }

    void ConfigureServices(IServiceCollection services);

    /// <summary>Maps the module's view models to their views.</summary>
    void RegisterViews(IViewRegistry views);

    IEnumerable<NavigationItem> GetNavigationItems();
}

/// <summary>An entry of the main navigation. The view model is created from DI when the entry is opened.</summary>
public sealed record NavigationItem(
    string Id,
    string Title,
    Type ViewModelType,
    string? RequiredPermission = null,
    int Order = 0);

/// <summary>A page view model that loads its data the first time it is shown.</summary>
public interface IPage
{
    Task OnFirstShownAsync();
}

/// <summary>View model type to view factory. The factory returns a UI control (typed as object so Core stays UI-free).</summary>
public interface IViewRegistry
{
    void Register(Type viewModelType, Func<object> createView);

    object? CreateView(object viewModel);
}

public static class ViewRegistryExtensions
{
    public static void Register<TViewModel, TView>(this IViewRegistry registry)
        where TView : new()
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(typeof(TViewModel), () => new TView());
    }
}
