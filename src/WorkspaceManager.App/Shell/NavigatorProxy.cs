using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.App.Shell;

/// <summary>
/// INavigator for the pages. The shell sets itself here when it is created; pages that are built by the shell
/// can then navigate without a circular dependency on it.
/// </summary>
public sealed class NavigatorProxy : INavigator
{
    public ShellViewModel? Shell { get; set; }

    public void Navigate(string navigationId) => Shell?.Open(navigationId);
}
