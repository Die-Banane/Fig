using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fig.Core.Timeline;

namespace Fig.App.ViewModels;

/// <summary>The Text and Position tabs: a title's words and look, and where a drawn layer sits in the frame.</summary>
public partial class PropertiesViewModel
{
    // text
    [ObservableProperty] private string _textValue = "";
    [ObservableProperty] private string _fontFamilyValue = TextStyle.Default.FontFamily;
    [ObservableProperty] private bool _textBold;
    [ObservableProperty] private bool _textItalic;
    [ObservableProperty] private double _textSizeValue = TextStyle.Default.SizePercent;
    [ObservableProperty] private string _textColorHex = "#FFFFFFFF";
    [ObservableProperty] private TextAlign _textAlignValue = TextAlign.Center;
    [ObservableProperty] private bool _textShadow;
    [ObservableProperty] private bool _textBackground;
    [ObservableProperty] private string _textBackgroundHex = "#B0000000";

    // position
    [ObservableProperty] private double _layoutX = 0.5;
    [ObservableProperty] private double _layoutY = 0.5;
    [ObservableProperty] private double _layoutScale = 1;
    [ObservableProperty] private double _layoutRotation;

    public static IReadOnlyList<TextAlign> TextAligns { get; } = Enum.GetValues<TextAlign>();

    private IReadOnlyList<string>? _fontFamilies;

    /// <summary>The fonts installed on this machine, by name.</summary>
    public IReadOnlyList<string> FontFamilies => _fontFamilies ??= FontManager.Current.SystemFonts
        .Select(f => f.Name).Append(TextStyle.Default.FontFamily)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    public IBrush TextColorBrush => Swatch(TextColorHex);
    public IBrush TextBackgroundBrush => Swatch(TextBackgroundHex);

    /// <summary>Crop sits in the Picture tab, for pictures that can be cropped.</summary>
    public bool HasCrop => Has(Facet.Crop);

    private void LoadLayer(Clip? clip)
    {
        OnPropertyChanged(nameof(HasCrop));
        if (clip is TextClip text)
        {
            var style = text.Style;
            TextValue = text.Text;
            FontFamilyValue = style.FontFamily;
            TextBold = style.Bold;
            TextItalic = style.Italic;
            TextSizeValue = style.SizePercent;
            TextColorHex = Hex(style.Color);
            TextAlignValue = style.Align;
            TextShadow = style.Shadow;
            TextBackground = style.Background;
            TextBackgroundHex = Hex(style.BackgroundColor);
        }
        if (clip is ILayerClip layer)
            (LayoutX, LayoutY, LayoutScale, LayoutRotation) =
                (layer.Layout.CenterX, layer.Layout.CenterY, layer.Layout.Scale, layer.Layout.Rotation);
    }

    /// <summary>The clips selected on the timeline (the primary one last).</summary>
    private IEnumerable<string> SelectedClipIds => _editor.Editor.Selected.OfType<Clip>().Select(c => c.Id);

    /// <summary>
    /// Writes one inspector value to every selected clip that has <paramref name="facet"/> (and the clips linked
    /// to them), as one undo step; a slider drag coalesces into it. Each <paramref name="property"/> name must be
    /// unique to its setter: edits coalesce by name.
    /// </summary>
    private void SetOnSelection<T>(Facet facet, string property, Func<Clip, T> get, Action<Clip, T> set, T value)
    {
        if (_suppressApply || !Has(facet))
            return;
        _editor.Editor.SetClipValue(_editor.Editor.EditTargets(SelectedClipIds, facet), property, get, set, value);
        _editor.Preview.RefreshFrame();
    }

    private void SetStyle<T>(string property, Func<TextStyle, T> get, Func<TextStyle, T, TextStyle> with, T value)
        => SetOnSelection(Facet.Text, property,
            c => get(((TextClip)c).Style),
            (c, v) => ((TextClip)c).Style = with(((TextClip)c).Style, v),
            value);

    private void SetLayout(string property, Func<LayerTransform, double> get, Func<LayerTransform, double, LayerTransform> with, double value)
        => SetOnSelection(Facet.Layout, property,
            c => get(((ILayerClip)c).Layout),
            (c, v) => ((ILayerClip)c).Layout = with(((ILayerClip)c).Layout, v),
            value);

    partial void OnTextValueChanged(string value)
        => SetOnSelection(Facet.Text, "text", c => ((TextClip)c).Text, (c, v) => ((TextClip)c).Text = v, value);

    partial void OnFontFamilyValueChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SetStyle("font", s => s.FontFamily, (s, v) => s with { FontFamily = v }, value);
    }

    partial void OnTextBoldChanged(bool value) => SetStyle("bold", s => s.Bold, (s, v) => s with { Bold = v }, value);
    partial void OnTextItalicChanged(bool value) => SetStyle("italic", s => s.Italic, (s, v) => s with { Italic = v }, value);

    partial void OnTextSizeValueChanged(double value)
        => SetStyle("text size", s => s.SizePercent, (s, v) => s with { SizePercent = v }, Math.Clamp(value, 1, 40));

    partial void OnTextAlignValueChanged(TextAlign value) => SetStyle("alignment", s => s.Align, (s, v) => s with { Align = v }, value);
    partial void OnTextShadowChanged(bool value) => SetStyle("shadow", s => s.Shadow, (s, v) => s with { Shadow = v }, value);
    partial void OnTextBackgroundChanged(bool value) => SetStyle("background", s => s.Background, (s, v) => s with { Background = v }, value);

    partial void OnTextColorHexChanged(string value)
    {
        OnPropertyChanged(nameof(TextColorBrush));
        if (TryParseColor(value) is { } color)
            SetStyle("text colour", s => s.Color, (s, v) => s with { Color = v }, color);
    }

    partial void OnTextBackgroundHexChanged(string value)
    {
        OnPropertyChanged(nameof(TextBackgroundBrush));
        if (TryParseColor(value) is { } color)
            SetStyle("background colour", s => s.BackgroundColor, (s, v) => s with { BackgroundColor = v }, color);
    }

    partial void OnLayoutXChanged(double value) => SetLayout("horizontal position", l => l.CenterX, (l, v) => l with { CenterX = v }, value);
    partial void OnLayoutYChanged(double value) => SetLayout("vertical position", l => l.CenterY, (l, v) => l with { CenterY = v }, value);
    partial void OnLayoutScaleChanged(double value) => SetLayout("scale", l => l.Scale, (l, v) => l with { Scale = v }, Math.Max(0.05, value));
    partial void OnLayoutRotationChanged(double value) => SetLayout("rotation", l => l.Rotation, (l, v) => l with { Rotation = v }, value);

    /// <summary>Puts every selected layer back in the middle, unscaled and unrotated, as one undo step.</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        SetOnSelection(Facet.Layout, "reset position", c => ((ILayerClip)c).Layout,
            (c, v) => ((ILayerClip)c).Layout = v, LayerTransform.Default);
        Refresh();
    }

    private static string Hex(uint argb) => $"#{argb:X8}";

    private static uint? TryParseColor(string text) => Color.TryParse(text?.Trim(), out var color) ? color.ToUInt32() : null;

    private static IBrush Swatch(string hex) => Color.TryParse(hex?.Trim(), out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
}
