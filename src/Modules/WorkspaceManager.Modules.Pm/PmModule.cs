using Microsoft.Extensions.DependencyInjection;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Modules.Pm.Ai;
using WorkspaceManager.Modules.Pm.Report;
using WorkspaceManager.Modules.Pm.Repos;
using WorkspaceManager.Reports;

namespace WorkspaceManager.Modules.Pm;

public sealed class PmModule : IModule
{
    public string Id => BuiltInRoles.PmModuleId;

    public string DisplayName => "Project management";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ReposViewModel>();
        services.AddSingleton<IPmReportService, PmReportService>();
        services.AddSingleton<PmReportViewModel>();
        services.AddSingleton<AiReportService>();
        services.AddSingleton<AiReportViewModel>();
    }

    public void RegisterViews(IViewRegistry views)
    {
        views.Register<ReposViewModel, ReposView>();
        views.Register<PmReportViewModel, PmReportView>();
        views.Register<AiReportViewModel, AiReportView>();
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("pm.repos", "Repositories", typeof(ReposViewModel), PermissionNames.RepoRead, Order: 10),
        new("pm.report", "PM Report", typeof(PmReportViewModel), PermissionNames.RepoRead, Order: 20),
        new("pm.ai", "AI Report", typeof(AiReportViewModel), PermissionNames.RepoRead, Order: 30),
    ];
}
