using System.Globalization;
using Avalonia.Data.Converters;
using WorkspaceManager.Core.Reports;

namespace WorkspaceManager.Modules.Pm.Report;

/// <summary>Enum value -> bool, to switch style classes in XAML.</summary>
public sealed class EnumIs<T>(T expected) : IValueConverter
    where T : struct, Enum
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is T v && v.Equals(expected);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class LevelIs
{
    public static EnumIs<ActionLevel> High { get; } = new(ActionLevel.High);

    public static EnumIs<ActionLevel> Medium { get; } = new(ActionLevel.Medium);

    public static EnumIs<ActionLevel> Low { get; } = new(ActionLevel.Low);

    public static EnumIs<ActionLevel> Info { get; } = new(ActionLevel.Info);
}

public static class ToneIs
{
    public static EnumIs<KpiTone> Warning { get; } = new(KpiTone.Warning);

    public static EnumIs<KpiTone> Bad { get; } = new(KpiTone.Bad);
}
