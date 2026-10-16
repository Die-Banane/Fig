using System.Collections.Generic;

namespace Fig.Core.Timeline
{
    /// <summary>A ready-made title in the library's Text tab: what it says, how it looks and where it sits.</summary>
    public sealed record TextPreset(string Name, string Text, TextStyle Style, LayerTransform Layout, double DurSec = 3);

    public static class TextPresets
    {
        public static IReadOnlyList<TextPreset> All { get; } =
        [
            new("Title", "Title", TextStyle.Default, LayerTransform.Default),
            new("Subtitle", "Subtitle", TextStyle.Default with { Bold = false, SizePercent = 5 },
                LayerTransform.Default with { CenterY = 0.62 }),
            new("Lower third", "Name\nRole", TextStyle.Default with { SizePercent = 4.5, Align = TextAlign.Left, Background = true },
                LayerTransform.Default with { CenterX = 0.24, CenterY = 0.82 }),
            new("Caption", "Caption", TextStyle.Default with { Bold = false, SizePercent = 4, Shadow = false, Background = true },
                LayerTransform.Default with { CenterY = 0.9 }),
            new("Big statement", "BIG", TextStyle.Default with { SizePercent = 18 }, LayerTransform.Default),
            new("Credits", "Directed by\nYour Name", TextStyle.Default with { Bold = false, Italic = true, SizePercent = 5 },
                LayerTransform.Default),
        ];
    }
}
