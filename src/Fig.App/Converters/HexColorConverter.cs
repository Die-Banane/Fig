using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fig.App.Converters;

/// <summary>
/// A colour written as hex ("#AARRGGBB", "#RRGGBB", or without the #) to a <see cref="Color"/> and back, so a
/// colour picker can edit a value the view models keep as hex.
/// </summary>
public sealed class HexColorConverter : IValueConverter
{
    public static readonly HexColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim() ?? "";
        if (text.Length is 6 or 8 && !text.StartsWith('#'))
            text = "#" + text;
        return Color.TryParse(text, out var color) ? color : Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color color ? $"#{color.ToUInt32():X8}" : BindingOperations.DoNothing;
}
