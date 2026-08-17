using Avalonia.Media;

namespace Fig.App.Services;

/// <summary>Shared editor colors for AXAML and custom-drawn controls.</summary>
public static class EditorTheme
{
    public static readonly Color Surface = Color.Parse("#18191c");
    public static readonly Color Card = Color.Parse("#202226");
    public static readonly Color Border = Color.Parse("#34373c");
    public static readonly Color TextPrimary = Color.Parse("#d4d7dc");
    public static readonly Color TextMuted = Color.Parse("#858b94");
    public static readonly Color Accent = Color.Parse("#5795df");
    public static readonly Color Playhead = Color.Parse("#f0645a");
    public static readonly Color TrackLane = Color.Parse("#202226");
    public static readonly Color TrackLaneAlt = Color.Parse("#1d1f22");
    public static readonly Color RulerBackground = Color.Parse("#181a1d");
    public static readonly Color SelectedLaneTint = Color.FromArgb(20, 0x7b, 0x9f, 0xc6);

    public static readonly IBrush SurfaceBrush = new SolidColorBrush(Surface);
    public static readonly IBrush CardBrush = new SolidColorBrush(Card);
    public static readonly IBrush BorderBrush = new SolidColorBrush(Border);
    public static readonly IBrush TextPrimaryBrush = new SolidColorBrush(TextPrimary);
    public static readonly IBrush TextMutedBrush = new SolidColorBrush(TextMuted);
    public static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    public static readonly IBrush PlayheadBrush = new SolidColorBrush(Playhead);
}
