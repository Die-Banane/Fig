using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fig.App.Converters
{
    /// <summary>Text-style flags as font properties, so a library title card looks like the title it adds.</summary>
    public static class LayerConverters
    {
        public static readonly IValueConverter Weight =
            new FuncValueConverter<bool, FontWeight>(bold => bold ? FontWeight.Bold : FontWeight.Normal);

        public static readonly IValueConverter Slant =
            new FuncValueConverter<bool, FontStyle>(italic => italic ? FontStyle.Italic : FontStyle.Normal);
    }
}
