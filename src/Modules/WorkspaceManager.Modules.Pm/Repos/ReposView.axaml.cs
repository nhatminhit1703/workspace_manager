using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WorkspaceManager.Modules.Pm.Repos;

public partial class ReposView : UserControl
{
    private ReposViewModel? _vm;

    public ReposView()
    {
        InitializeComponent();
        Grid.CellEditEnded += (_, _) => _vm?.SaveSelection();
        Grid.PointerReleased += (_, _) => _vm?.SaveSelection();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Log.CollectionChanged -= OnLogChanged;
        }

        _vm = DataContext as ReposViewModel;
        if (_vm is not null)
        {
            _vm.Log.CollectionChanged += OnLogChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
}
