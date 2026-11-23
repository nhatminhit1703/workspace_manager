using Avalonia.Controls;

namespace WorkspaceManager.Modules.Pm.Repos;

public partial class ReposView : UserControl
{
    public ReposView()
    {
        InitializeComponent();
        Grid.CellEditEnded += (_, _) => (DataContext as ReposViewModel)?.SaveSelection();
        Grid.PointerReleased += (_, _) => (DataContext as ReposViewModel)?.SaveSelection();
    }
}
