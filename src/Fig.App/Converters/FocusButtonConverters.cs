using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fig.App.Converters;

/// <summary>"Focus preview" ⇄ "Restore" label for the preview header button.</summary>
public sealed class BoolToFocusButtonTextConverter : IValueConverter
{
    public static readonly BoolToFocusButtonTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Restore" : "Focus preview";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Keeps the preview header button wide enough for its current label.</summary>
public sealed class BoolToFocusButtonWidthConverter : IValueConverter
{
    public static readonly BoolToFocusButtonWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 104.0 : 140.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
