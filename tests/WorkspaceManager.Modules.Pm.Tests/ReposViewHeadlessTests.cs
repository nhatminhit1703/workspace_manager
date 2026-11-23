using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using WorkspaceManager.Core.Settings;
using WorkspaceManager.Modules.Pm.Repos;

[assembly: AvaloniaTestApplication(typeof(WorkspaceManager.Modules.Pm.Tests.HeadlessApp))]

namespace WorkspaceManager.Modules.Pm.Tests;

public sealed class HeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
    }
}

/// <summary>Renders the real view without a screen: catches XAML / binding mistakes that unit tests miss.</summary>
public class ReposViewHeadlessTests
{
    [AvaloniaFact]
    public async Task Grid_shows_one_row_per_repository()
    {
        var vm = Make.ViewModel(new FakeScanner("api", "site"), new FakeReader(p => Make.Status(p, changes: 1)),
            new MemorySettings(new AppSettings { WorkspaceRoot = Path.GetTempPath() }));
        await vm.RefreshAsync();

        var window = new Window { Width = 1200, Height = 700, Content = new ReposView { DataContext = vm } };
        window.Show();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.IsVisible.Should().BeTrue();
        grid.ItemsSource.Cast<object>().Should().HaveCount(2);
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("2 repositories | 2 with uncommitted changes | 0 behind upstream");
    }

    [AvaloniaFact]
    public void Empty_state_without_workspace()
    {
        var vm = Make.ViewModel(new FakeScanner(), new FakeReader(p => Make.Status(p)), new MemorySettings());

        var window = new Window { Content = new ReposView { DataContext = vm } };
        window.Show();

        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("No workspace folder yet");
        window.GetVisualDescendants().OfType<DataGrid>().Single().IsVisible.Should().BeFalse();
    }
}
