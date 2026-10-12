using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.ViewModels;

namespace Fig.App.Controls;

/// <summary>
/// Handles for the Swirl transition drawn over the preview: drag the center point to move the
/// swirl, the handle on the circle's right to change its radius, and the handle on the arc to
/// change how far it turns.
/// </summary>
public sealed class SwirlOverlay : Control
{
    private enum Grab { None, Center, Radius, Rotation }

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#5f9cf8"));
    private static readonly IBrush Chip = new SolidColorBrush(Color.Parse("#151618"));
    private static readonly Pen CirclePen = new(Accent, 1.5, new DashStyle(new double[] { 6, 5 }, 0));
    private static readonly Pen ArcPen = new(Accent, 2);
    private static readonly Pen HandlePen = new(Accent, 1.5);

    private SwirlControlsViewModel? _vm;
    private Grab _grab;

    public SwirlOverlay()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as SwirlControlsViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
            InvalidateVisual();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private Rect PictureRect()
    {
        var aspect = _vm?.FrameAspect > 0 ? _vm.FrameAspect : 16.0 / 9;
        var w = Bounds.Width;
        var h = w / aspect;
        if (h > Bounds.Height)
        {
            h = Bounds.Height;
            w = h * aspect;
        }
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }

    private Point Center(Rect picture) => new(picture.X + _vm!.CenterX * picture.Width, picture.Y + _vm.CenterY * picture.Height);

    private double RadiusPx(Rect picture) => _vm!.Radius * Math.Min(picture.Width, picture.Height);

    /// <summary>The rotation handle sits on the circle: straight up at 0°, turning with the swirl.</summary>
    private Point RotationHandle(Rect picture)
    {
        var angle = (-90 + (_vm!.Clockwise ? 1 : -1) * _vm.RotationDegrees) * Math.PI / 180;
        var c = Center(picture);
        var r = RadiusPx(picture);
        return new Point(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));
    }

    /// <summary>The radius handle sits at the lower left so it stays clear of the rotation handle's usual path.</summary>
    private Point RadiusHandle(Rect picture)
    {
        var c = Center(picture);
        var r = RadiusPx(picture);
        return new Point(c.X - r * Math.Sqrt(0.5), c.Y + r * Math.Sqrt(0.5));
    }

    private static Rect Box(Point p, double size = 10) => new(p.X - size / 2, p.Y - size / 2, size, size);

    public override void Render(DrawingContext context)
    {
        // a transparent fill makes the whole area take part in hit testing (a bare Control would not)
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_vm is null || !_vm.ShowOverlay)
            return;
        var picture = PictureRect();
        var c = Center(picture);
        var r = RadiusPx(picture);

        context.DrawEllipse(null, CirclePen, c, r, r);

        // the arc from "up" to the rotation handle shows how far (and which way) it turns
        var sweep = Math.Min(_vm.RotationDegrees, 359.9) * (_vm.Clockwise ? 1 : -1);
        var start = new Point(c.X, c.Y - r);
        var end = RotationHandle(picture);
        var arc = new StreamGeometry();
        using (var gc = arc.Open())
        {
            gc.BeginFigure(start, false);
            gc.ArcTo(end, new Size(r, r), 0, Math.Abs(sweep) > 180,
                sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            gc.EndFigure(false);
        }
        context.DrawGeometry(null, ArcPen, arc);

        context.DrawRectangle(Brushes.White, HandlePen, Box(c, 11));
        context.DrawRectangle(Brushes.White, HandlePen, Box(RadiusHandle(picture)));
        context.DrawRectangle(Brushes.White, HandlePen, Box(end));

        DrawChip(context, "Drag center", new Point(c.X + 14, c.Y + 12), picture);
        DrawChip(context, $"Radius · {_vm.Radius * 100:0}%", new Point(c.X + r + 10, c.Y - 10), picture);
        DrawChip(context, $"Rotation · {_vm.RotationDegrees:0}°", new Point(end.X + 12, end.Y - 26), picture);
    }

    private static void DrawChip(DrawingContext context, string text, Point at, Rect limit)
    {
        var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
        var box = new Rect(at.X, at.Y, label.Width + 18, label.Height + 8);
        // keep the chip on the picture
        var x = Math.Clamp(box.X, limit.X + 2, Math.Max(limit.X + 2, limit.Right - box.Width - 2));
        var y = Math.Clamp(box.Y, limit.Y + 2, Math.Max(limit.Y + 2, limit.Bottom - box.Height - 2));
        box = new Rect(x, y, box.Width, box.Height);
        context.DrawRectangle(Chip, null, box, 3, 3);
        context.DrawText(label, new Point(box.X + 9, box.Y + 4));
    }

    private Grab HitTest(Point p, Rect picture)
    {
        var c = Center(picture);
        var r = RadiusPx(picture);
        if (Box(RotationHandle(picture), 16).Contains(p)) return Grab.Rotation;
        if (Box(RadiusHandle(picture), 16).Contains(p)) return Grab.Radius;
        if (Box(c, 18).Contains(p)) return Grab.Center;
        return Grab.None;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !_vm.ShowOverlay || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _grab = HitTest(e.GetPosition(this), PictureRect());
        if (_grab == Grab.None)
            return;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null || !_vm.ShowOverlay)
            return;
        var p = e.GetPosition(this);
        var picture = PictureRect();

        if (_grab == Grab.None)
        {
            Cursor = HitTest(p, picture) == Grab.None ? Cursor.Default : new Cursor(StandardCursorType.SizeAll);
            return;
        }

        var c = Center(picture);
        switch (_grab)
        {
            case Grab.Center:
                _vm.Set(centerX: (p.X - picture.X) / picture.Width, centerY: (p.Y - picture.Y) / picture.Height);
                break;
            case Grab.Radius:
            {
                var distance = Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
                _vm.Set(radius: distance / Math.Min(picture.Width, picture.Height));
                break;
            }
            case Grab.Rotation:
            {
                var angle = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI;   // 0° = right
                var turned = (angle + 90 + 360) % 360;                          // 0° = straight up, clockwise
                _vm.Set(rotation: _vm.Clockwise ? turned : (360 - turned) % 360);
                break;
            }
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_grab != Grab.None && e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        _grab = Grab.None;
    }
}
