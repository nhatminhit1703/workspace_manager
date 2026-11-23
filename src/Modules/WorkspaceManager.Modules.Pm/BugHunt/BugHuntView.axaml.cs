using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace WorkspaceManager.Modules.Pm.BugHunt;

public partial class BugHuntView : UserControl
{
    public BugHuntView()
    {
        InitializeComponent();
    }
}

/// <summary>Text equals -> bool, to switch style classes in XAML.</summary>
public sealed class TextIs(string expected) : IValueConverter
{
    public static TextIs Critical { get; } = new("CRITICAL");

    public static TextIs High { get; } = new("HIGH");

    public static TextIs Medium { get; } = new("MEDIUM");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, expected, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
