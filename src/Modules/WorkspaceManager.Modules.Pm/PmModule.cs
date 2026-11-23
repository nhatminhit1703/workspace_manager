using Microsoft.Extensions.DependencyInjection;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Modules.Pm.Ai;
using WorkspaceManager.Modules.Pm.BugHunt;
using WorkspaceManager.Modules.Pm.Dashboard;
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
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ReposViewModel>();
        services.AddSingleton<IPmReportService, PmReportService>();
        services.AddSingleton<PmReportViewModel>();
        services.AddSingleton<AiReportService>();
        services.AddSingleton<AiReportViewModel>();
        services.AddSingleton<BugHuntService>();
        services.AddSingleton<BugHuntViewModel>();
    }

    public void RegisterViews(IViewRegistry views)
    {
        views.Register<DashboardViewModel, DashboardView>();
        views.Register<ReposViewModel, ReposView>();
        views.Register<PmReportViewModel, PmReportView>();
        views.Register<AiReportViewModel, AiReportView>();
        views.Register<BugHuntViewModel, BugHuntView>();
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("pm.dashboard", "Dashboard", typeof(DashboardViewModel), PermissionNames.RepoRead, Order: 0),
        new("pm.repos", "Repositories", typeof(ReposViewModel), PermissionNames.RepoRead, Order: 10),
        new("pm.report", "PM Report", typeof(PmReportViewModel), PermissionNames.RepoRead, Order: 20),
        new("pm.ai", "AI Report", typeof(AiReportViewModel), PermissionNames.RepoRead, Order: 30),
        new("pm.bughunt", "Bug Hunt", typeof(BugHuntViewModel), PermissionNames.RepoRead, Order: 40),
    ];
}
