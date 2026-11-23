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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sinceStart = (int)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            var dialogs = new AvaloniaDialogService();
            var options = AppOptions.Parse(desktop.Args ?? []);
            _host = AppHost.Build(options, dialogs);
            var tHost = sw.ElapsedMilliseconds;
            Log.Information("Workspace Manager {Version} started", ShellViewModel.Version);

            DataTemplates.Add(_host.Services.GetRequiredService<ViewRegistry>());
            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            shell.Open(options.Page);
            var tShell = sw.ElapsedMilliseconds;
            var window = new MainWindow { DataContext = shell };
            var tWindow = sw.ElapsedMilliseconds;
            Log.Information("Startup: velopack {Vp} ms, framework ready {Start} ms after process start; host {Host} ms, shell {Shell} ms, window {Window} ms", Program.VelopackMs,
                sinceStart, tHost, tShell - tHost, tWindow - tShell);
            window.Opened += (_, _) =>
            {
                var startup = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
                Log.Information("Main window opened {Ms} ms after process start", (int)startup.TotalMilliseconds);
                shell.OnWindowShown();
            };
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
