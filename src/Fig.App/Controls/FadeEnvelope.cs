using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fig.App.Controls;

/// <summary>Small preview of a clip's fade-in / full level / fade-out shape.</summary>
public sealed class FadeEnvelope : Control
{
    public static readonly StyledProperty<double> FadeInProperty =
        AvaloniaProperty.Register<FadeEnvelope, double>(nameof(FadeIn));

    public static readonly StyledProperty<double> FadeOutProperty =
        AvaloniaProperty.Register<FadeEnvelope, double>(nameof(FadeOut));

    public static readonly StyledProperty<double> ClipDurationProperty =
        AvaloniaProperty.Register<FadeEnvelope, double>(nameof(ClipDuration), 1);

    public double FadeIn { get => GetValue(FadeInProperty); set => SetValue(FadeInProperty, value); }
    public double FadeOut { get => GetValue(FadeOutProperty); set => SetValue(FadeOutProperty, value); }
    public double ClipDuration { get => GetValue(ClipDurationProperty); set => SetValue(ClipDurationProperty, value); }

    static FadeEnvelope()
    {
        AffectsRender<FadeEnvelope>(FadeInProperty, FadeOutProperty, ClipDurationProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#0F1012")), new Pen(new SolidColorBrush(Color.Parse("#2B2D31")), 1), bounds, 4, 4);

        const double pad = 12;
        var left = pad;
        var right = bounds.Width - pad;
        var top = pad;
        var bottom = bounds.Height - pad;
        var width = right - left;
        var duration = System.Math.Max(0.01, ClipDuration);
        var fadeIn = System.Math.Clamp(FadeIn / duration, 0, 1) * width;
        var fadeOut = System.Math.Clamp(FadeOut / duration, 0, 1) * width;
        if (fadeIn + fadeOut > width)
        {
            var scale = width / (fadeIn + fadeOut);
            fadeIn *= scale;
            fadeOut *= scale;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(left, bottom), false);
            g.LineTo(new Point(left + fadeIn, top));
            g.LineTo(new Point(right - fadeOut, top));
            g.LineTo(new Point(right, bottom));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#5F9CF8")), 1.5, lineJoin: PenLineJoin.Round), geometry);
    }
}
