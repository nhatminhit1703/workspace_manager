using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WorkspaceManager.App.Shell;
using WorkspaceManager.App.Updates;
using WorkspaceManager.Core.Modules;
using WorkspaceManager.Core.Platform;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Core.Updates;
using WorkspaceManager.Data;
using WorkspaceManager.Git;
using WorkspaceManager.Modules.Pm;
using WorkspaceManager.Platform;

namespace WorkspaceManager.App;

/// <summary>Composition root: services, modules and the active role.</summary>
public static class AppHost
{
    /// <summary>Every module of the application. A role picks the ones it shows.</summary>
    public static IReadOnlyList<IModule> AllModules() => [new PmModule()];

    public static IHost Build(AppOptions options, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(options);
        var platform = PlatformServices.CreatePlatformService();
        var appData = options.AppDataDirectory ?? platform.AppDataDirectory;
        Directory.CreateDirectory(appData);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(appData, "logs", "wm-.log"), formatProvider: System.Globalization.CultureInfo.InvariantCulture, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSerilog();
        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(dialogs);
        builder.Services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(Path.Combine(appData, "settings.json")));
        if (OperatingSystem.IsWindows())
        {
            builder.Services.AddSingleton(_ => PlatformServices.CreateSecretStore(appData));
        }

        builder.Services.AddSingleton(new GitRunnerOptions());
        builder.Services.AddSingleton<IGitRunner, GitRunner>();
        builder.Services.AddSingleton<IRepoStatusReader, RepoStatusReader>();
        builder.Services.AddSingleton<IWorkspaceScanner, WorkspaceScanner>();
        builder.Services.AddSingleton<IRepoLocks, RepoLocks>();
        builder.Services.AddSingleton<ISyncService, SyncService>();
        builder.Services.AddSingleton<IRepoInsightReader, RepoInsightReader>();

        var role = BuiltInRoles.ProjectManager;
        var modules = AllModules().Where(m => role.ModuleIds.Contains(m.Id)).ToList();
        var views = new ViewRegistry();
        foreach (var m in modules)
        {
            m.ConfigureServices(builder.Services);
            m.RegisterViews(views);
        }

        builder.Services.AddSingleton(views);
        builder.Services.AddSingleton<IViewRegistry>(views);
        builder.Services.AddSingleton(role);
        builder.Services.AddSingleton<IReadOnlyList<IModule>>(modules);
        builder.Services.AddSingleton<IUpdateService, VelopackUpdateService>();
        builder.Services.AddSingleton<UpdateBannerViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();

        var host = builder.Build();
        PrepareSettings(host.Services.GetRequiredService<ISettingsStore>(), options);
        return host;
    }

    private static void PrepareSettings(ISettingsStore store, AppOptions options)
    {
        var s = store.Load();
        var changed = LegacySettingsImporter.Import(s, LegacySettingsImporter.DefaultLegacyStateFile) || !s.LegacyImportDone;
        s.LegacyImportDone = true;
        if (options.WorkspaceRoot is not null && Directory.Exists(options.WorkspaceRoot))
        {
            s.WorkspaceRoot = Path.GetFullPath(options.WorkspaceRoot);
            changed = true;
        }

        if (changed)
        {
            store.Save(s);
        }
    }
}

/// <summary>Command line: <c>--workspace &lt;folder&gt;</c>, <c>--page &lt;navigation id&gt;</c>, <c>--appdata &lt;folder&gt;</c> (tests).</summary>
public sealed record AppOptions(string? WorkspaceRoot, string? AppDataDirectory, string? Page = null)
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? workspace = null, appData = null, page = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            switch (args[i])
            {
                case "--workspace":
                    workspace = args[++i];
                    break;
                case "--appdata":
                    appData = args[++i];
                    break;
                case "--page":
                    page = args[++i];
                    break;
                default:
                    break;
            }
        }

        return new AppOptions(workspace, appData, page);
    }
}
