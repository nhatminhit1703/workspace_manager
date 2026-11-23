using Avalonia.Controls;
using Avalonia.Controls.Templates;
using WorkspaceManager.Core.Modules;

namespace WorkspaceManager.App.Shell;

/// <summary>View model type -> view factory, filled by the modules. Used as the app-wide data template (no reflection).</summary>
public sealed class ViewRegistry : IViewRegistry, IDataTemplate
{
    private readonly Dictionary<Type, Func<object>> _factories = [];

    public void Register(Type viewModelType, Func<object> createView) => _factories[viewModelType] = createView;

    public object? CreateView(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return _factories.TryGetValue(viewModel.GetType(), out var f) ? f() : null;
    }

    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        return CreateView(param) as Control ?? new TextBlock { Text = "No view registered for " + param.GetType().Name };
    }

    public bool Match(object? data) => data is not null && _factories.ContainsKey(data.GetType());
}
