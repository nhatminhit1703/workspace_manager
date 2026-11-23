using Avalonia;
using Velopack;

namespace WorkspaceManager.App;

internal static class Program
{
    public static long VelopackMs { get; private set; }

    // Do not use Avalonia, third-party APIs or a SynchronizationContext before AppMain runs.
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles the install / update / uninstall hooks of the installer, then returns.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        VelopackApp.Build().Run();
        VelopackMs = sw.ElapsedMilliseconds;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
