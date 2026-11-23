using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WorkspaceManager.App.Shell;

namespace WorkspaceManager.App;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialogs = new AvaloniaDialogService();
            _host = AppHost.Build(AppOptions.Parse(desktop.Args ?? []), dialogs);
            Log.Information("Workspace Manager {Version} started", ShellViewModel.Version);

            DataTemplates.Add(_host.Services.GetRequiredService<ViewRegistry>());
            var window = new MainWindow { DataContext = _host.Services.GetRequiredService<ShellViewModel>() };
            dialogs.Owner = window;
            desktop.MainWindow = window;
            var updates = new CancellationTokenSource();
            _ = _host.Services.GetRequiredService<UpdateBannerViewModel>().RunAsync(updates.Token);
            desktop.Exit += (_, _) =>
            {
                updates.Cancel();
                updates.Dispose();
                _host.Dispose();
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
