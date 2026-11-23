using Microsoft.Extensions.DependencyInjection;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Modules.Pm.Repos;

namespace WorkspaceManager.Modules.Pm;

public sealed class PmModule : IModule
{
    public string Id => BuiltInRoles.PmModuleId;

    public string DisplayName => "Project management";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ReposViewModel>();
    }

    public void RegisterViews(IViewRegistry views) => views.Register<ReposViewModel, ReposView>();

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("pm.repos", "Repositories", typeof(ReposViewModel), PermissionNames.RepoRead, Order: 10),
    ];
}
