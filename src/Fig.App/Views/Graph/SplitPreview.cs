using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Fig.App.Views;

/// <summary>Before / after on one picture with a draggable divider.</summary>
public sealed class SplitPreview : Control
{
    public static readonly StyledProperty<Bitmap?> BeforeProperty = AvaloniaProperty.Register<SplitPreview, Bitmap?>(nameof(Before));
    public static readonly StyledProperty<Bitmap?> AfterProperty = AvaloniaProperty.Register<SplitPreview, Bitmap?>(nameof(After));
    public static readonly StyledProperty<double> SplitProperty = AvaloniaProperty.Register<SplitPreview, double>(nameof(Split), 0.5);

    public Bitmap? Before { get => GetValue(BeforeProperty); set => SetValue(BeforeProperty, value); }
    public Bitmap? After { get => GetValue(AfterProperty); set => SetValue(AfterProperty, value); }
    public double Split { get => GetValue(SplitProperty); set => SetValue(SplitProperty, value); }

    private bool _dragging;

    static SplitPreview() => AffectsRender<SplitPreview>(BeforeProperty, AfterProperty, SplitProperty);

    public SplitPreview() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        return new Size(width, width * 9 / 16);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var full = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101112")), null, full, 6, 6);
        var x = full.Width * Math.Clamp(Split, 0, 1);
        using (context.PushClip(new Rect(0, 0, x, full.Height)))
            if (Before is not null)
                context.DrawImage(Before, new Rect(Before.Size), full);
        using (context.PushClip(new Rect(x, 0, full.Width - x, full.Height)))
            if (After is not null)
                context.DrawImage(After, new Rect(After.Size), full);

        context.DrawRectangle(Brushes.White, null, new Rect(x - 1, 0, 2, full.Height));
        context.DrawEllipse(Brushes.White, null, new Point(x, full.Height / 2), 9, 9);
        Chip(context, "Before", 8);
        Chip(context, "After", full.Width - 58);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#3a3c42")), 1), full.Deflate(0.5), 6, 6);
    }

    private static void Chip(DrawingContext context, string text, double x)
    {
        var t = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 9, Brushes.White);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#101112")), null, new Rect(x, 8, 50, 18), 9, 9);
        context.DrawText(t, new Point(x + (50 - t.Width) / 2, 12));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        Update(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            Update(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void Update(PointerEventArgs e)
        => Split = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1);
}
