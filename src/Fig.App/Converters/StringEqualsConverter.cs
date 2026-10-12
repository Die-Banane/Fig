using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fig.App.Converters;

/// <summary>True when the two bound values are equal strings (used to mark the active filter chip).</summary>
public sealed class StringEqualsConverter : IMultiValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Count == 2 && Equals(values[0]?.ToString(), values[1]?.ToString());
}
