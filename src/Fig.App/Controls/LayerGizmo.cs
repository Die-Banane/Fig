using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Fig.App.ViewModels;
using Fig.Core.Media;

namespace Fig.App.Controls;

/// <summary>
/// Boxes round the selected text and image layers on the preview. Click a layer to select it (Ctrl or Shift adds
/// it), drag inside a box to move, a corner to scale, the knob above to turn (Shift: 15° steps). Moving snaps the
/// centre to the middle of the frame; hold Ctrl to place freely. Only layers and handles take the pointer: a click
/// anywhere else goes through to the preview.
/// </summary>
public sealed class LayerGizmo : Control, ICustomHitTest
{
    private enum Grab { None, Move, Scale, Rotate }

    private const double HandleSize = 9;
    private const double KnobOffset = 22;
    private const double KnobRadius = 5;

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#5f9cf8"));
    private static readonly Pen PrimaryPen = new(Accent, 1.5);
    private static readonly Pen OtherPen = new(Accent, 1, new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly Pen HoverPen = new(new SolidColorBrush(Color.FromArgb(150, 0x5f, 0x9c, 0xf8)), 1);
    private static readonly Pen HandlePen = new(Accent, 1);
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.Parse("#e0559a")), 1, new DashStyle(new double[] { 5, 4 }, 0));

    private LayerGizmoViewModel? _vm;
    private Grab _grab;
    private Point _press;
    private Point _center;
    private double _pressDistance;
    private double _pressAngle;
    // an unselected layer under the pointer, outlined to show a click would pick it
    private GizmoItem? _hover;

    public LayerGizmo()
    {
        ClipToBounds = true;
        Focusable = false;
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as LayerGizmoViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
            InvalidateVisual();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    /// <summary>Where the picture sits inside this control (letterboxed to keep its aspect).</summary>
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

    private static Point ToScreen(Rect picture, (double X, double Y) p)
        => new(picture.X + p.X * picture.Width, picture.Y + p.Y * picture.Height);

    private (double X, double Y) ToFrame(Rect picture, Point p)
        => ((p.X - picture.X) / picture.Width, (p.Y - picture.Y) / picture.Height);

    /// <summary>A layer's box on screen, and its turn knob just above the middle of its top edge.</summary>
    private (Point[] Corners, Point Knob) Box(Rect picture, GizmoItem item)
    {
        var aspect = _vm!.FrameAspect;
        var corners = LayerGeometry.Corners(item.Layout, item.Size, aspect).Select(c => ToScreen(picture, c)).ToArray();
        var center = ToScreen(picture, (item.Layout.CenterX, item.Layout.CenterY));
        var top = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
        var up = top - center;
        var length = Math.Sqrt(up.X * up.X + up.Y * up.Y);
        // a box squashed to nothing still needs a knob: point it the way the layer's top faces
        var direction = length > 1e-3 ? up / length
            : new Point(Math.Sin(item.Layout.Rotation * Math.PI / 180), -Math.Cos(item.Layout.Rotation * Math.PI / 180));
        return (corners, top + direction * KnobOffset);
    }

    private static Rect HandleAt(Point p) => new(p.X - HandleSize / 2, p.Y - HandleSize / 2, HandleSize, HandleSize);

    private static Geometry Outline(Point[] corners)
    {
        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(corners[0], false);
            for (var i = 1; i < corners.Length; i++)
                gc.LineTo(corners[i]);
            gc.EndFigure(true);
        }
        return geometry;
    }

    /// <summary>What a press at <paramref name="p"/> would grab on the primary layer's handles.</summary>
    private Grab HandleUnder(Point p)
    {
        if (_vm?.Primary is not { } primary)
            return Grab.None;
        var (corners, knob) = Box(PictureRect(), primary);
        var dx = p.X - knob.X;
        var dy = p.Y - knob.Y;
        if (dx * dx + dy * dy <= (KnobRadius + 4) * (KnobRadius + 4))
            return Grab.Rotate;
        return corners.Any(c => HandleAt(c).Inflate(4).Contains(p)) ? Grab.Scale : Grab.None;
    }

    private bool OverSelected(Point p)
    {
        var picture = PictureRect();
        var (x, y) = ToFrame(picture, p);
        return _vm!.Items.Any(item => LayerGeometry.Contains(item.Layout, item.Size, x, y, _vm.FrameAspect));
    }

    public bool HitTest(Point point)
    {
        if (_vm is null || !_vm.ShowOverlay)
            return false;
        if (_grab != Grab.None || HandleUnder(point) != Grab.None || OverSelected(point))
            return true;
        return LayerUnder(point) is not null;
    }

    /// <summary>The layer showing under a screen point (selected or not), with its box.</summary>
    private GizmoItem? LayerUnder(Point p)
    {
        var picture = PictureRect();
        var (x, y) = ToFrame(picture, p);
        return _vm!.ItemAt(x, y);
    }

    public override void Render(DrawingContext context)
    {
        if (_vm is null || !_vm.ShowOverlay)
            return;
        var picture = PictureRect();

        if (_vm.SnappedX)
            context.DrawLine(GuidePen, new Point(picture.Center.X, picture.Top), new Point(picture.Center.X, picture.Bottom));
        if (_vm.SnappedY)
            context.DrawLine(GuidePen, new Point(picture.Left, picture.Center.Y), new Point(picture.Right, picture.Center.Y));

        if (_hover is { } hover && _grab == Grab.None)
            context.DrawGeometry(null, HoverPen, Outline(Box(picture, hover).Corners));

        foreach (var item in _vm.Items)
        {
            var (corners, knob) = Box(picture, item);
            context.DrawGeometry(null, item.IsPrimary ? PrimaryPen : OtherPen, Outline(corners));
            if (!item.IsPrimary)
                continue;
            var top = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
            context.DrawLine(HandlePen, top, knob);
            context.DrawEllipse(Brushes.White, HandlePen, knob, KnobRadius, KnobRadius);
            foreach (var corner in corners)
                context.DrawRectangle(Brushes.White, HandlePen, HandleAt(corner));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !_vm.ShowOverlay || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetPosition(this);
        var additive = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
        var picture = PictureRect();
        _grab = HandleUnder(p);
        if (_grab == Grab.None)
        {
            var (x, y) = ToFrame(picture, p);
            if (!_vm.Pick(x, y, additive))
                return;
            // Ctrl/Shift-click only changes the selection; a plain press goes on to move what is under it
            _grab = additive ? Grab.None : Grab.Move;
        }
        e.Handled = true;
        _hover = null;
        if (_grab == Grab.None || _vm.Primary is not { } primary)
        {
            InvalidateVisual();
            return;
        }

        _press = p;
        _center = ToScreen(picture, (primary.Layout.CenterX, primary.Layout.CenterY));
        var offset = p - _center;
        _pressDistance = Math.Max(1, Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y));
        _pressAngle = Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;
        _vm.Begin();
        e.Pointer.Capture(this);
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
            var handle = HandleUnder(p);
            var hovered = handle == Grab.None ? LayerUnder(p) : null;
            Cursor = handle switch
            {
                Grab.Scale => new Cursor(StandardCursorType.BottomRightCorner),
                Grab.Rotate => new Cursor(StandardCursorType.Hand),
                _ => hovered is not null ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default,
            };
            var hover = hovered is not null && _vm.Items.All(item => item.ClipId != hovered.ClipId) ? hovered : null;
            if (hover != _hover)
            {
                _hover = hover;
                InvalidateVisual();
            }
            return;
        }

        switch (_grab)
        {
            case Grab.Move:
                _vm.Move((p.X - _press.X) / picture.Width, (p.Y - _press.Y) / picture.Height,
                    snap: (e.KeyModifiers & KeyModifiers.Control) == 0);
                break;
            case Grab.Scale:
            {
                var offset = p - _center;
                _vm.Scale(Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) / _pressDistance);
                break;
            }
            case Grab.Rotate:
            {
                var offset = p - _center;
                var turn = Math.Atan2(offset.Y, offset.X) * 180 / Math.PI - _pressAngle;
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                    turn = Math.Round((_vm.StartRotation + turn) / 15) * 15 - _vm.StartRotation;
                _vm.Rotate(turn);
                break;
            }
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_grab == Grab.None)
            return;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        _grab = Grab.None;
        _vm?.End();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_grab == Grab.None)
            return;
        _grab = Grab.None;
        _vm?.End();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is null)
            return;
        _hover = null;
        InvalidateVisual();
    }
}
