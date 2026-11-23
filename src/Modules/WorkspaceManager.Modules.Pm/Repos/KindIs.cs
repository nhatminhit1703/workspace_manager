using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkspaceManager.Modules.Pm.Repos;

/// <summary>Log line kind -> bool, to switch style classes in XAML.</summary>
public sealed class KindIs(string kind) : IValueConverter
{
    public static KindIs Fail { get; } = new("fail");

    public static KindIs Skip { get; } = new("skip");

    public static KindIs Ok { get; } = new("ok");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, kind, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
