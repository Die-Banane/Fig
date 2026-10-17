using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.ViewModels;

namespace Fig.App.Controls;

/// <summary>
/// Draws the Transform camera over the preview: the frame (what will be seen) at the playhead and
/// the path the camera takes through its keyframes. Drag the frame to move it, or a corner to zoom;
/// the change is written as a keyframe at the playhead. The aspect ratio stays locked to the picture.
/// </summary>
public sealed class TransformOverlay : PreviewOverlay<TransformControlsViewModel>
{
    private enum Grab { None, Move, Corner }

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#5f9cf8"));
    private static readonly IBrush Chip = new SolidColorBrush(Color.Parse("#151618"));
    private static readonly Pen FramePen = new(Accent, 1.5);
    private static readonly Pen PathPen = new(new SolidColorBrush(Color.FromArgb(200, 0x5f, 0x9c, 0xf8)), 1.5, new DashStyle(new double[] { 5, 4 }, 0), PenLineCap.Round);
    private static readonly Pen HandlePen = new(Accent, 1);

    private Grab _grab;
    private Point _lastPoint;




    private Rect FrameRect(Rect picture)
    {
        var size = 1.0 / Math.Max(1, Model!.Zoom);
        var left = Math.Clamp(Model.CenterX - size / 2, 0, 1 - size);
        var top = Math.Clamp(Model.CenterY - size / 2, 0, 1 - size);
        return new Rect(picture.X + left * picture.Width, picture.Y + top * picture.Height, size * picture.Width, size * picture.Height);
    }

    private static Rect[] Handles(Rect r)
    {
        const double s = 9;
        return new[]
        {
            new Rect(r.X - s / 2, r.Y - s / 2, s, s), new Rect(r.Right - s / 2, r.Y - s / 2, s, s),
            new Rect(r.X - s / 2, r.Bottom - s / 2, s, s), new Rect(r.Right - s / 2, r.Bottom - s / 2, s, s),
        };
    }

    public override void Render(DrawingContext context)
    {
        // a transparent fill makes the whole area take part in hit testing (a bare Control would not)
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Model is null || !Model.ShowOverlay)
            return;

        var picture = PictureRect();
        var frame = FrameRect(picture);

        // the route the camera takes through its keyframes
        if (Model.Path.Count > 1)
        {
            var path = new StreamGeometry();
            using (var gc = path.Open())
            {
                for (var i = 0; i < Model.Path.Count; i++)
                {
                    var p = new Point(picture.X + Model.Path[i].X * picture.Width, picture.Y + Model.Path[i].Y * picture.Height);
                    if (i == 0) gc.BeginFigure(p, false);
                    else gc.LineTo(p);
                }
                gc.EndFigure(false);
            }
            context.DrawGeometry(null, PathPen, path);
            foreach (var (x, y) in Model.Path)
                context.DrawEllipse(Accent, null, new Point(picture.X + x * picture.Width, picture.Y + y * picture.Height), 3.5, 3.5);
        }

        // dim everything outside the frame so it reads as "what you will see"
        var dim = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        context.DrawGeometry(dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(picture), new RectangleGeometry(frame)));

        context.DrawRectangle(null, FramePen, frame);
        foreach (var handle in Handles(frame))
            context.DrawRectangle(Brushes.White, HandlePen, handle);

        var text = $"{Model.Zoom:0.00}×" + (Model.IsOnKeyframe ? " · keyframe" : "");
        var label = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
        var chip = new Rect(frame.X, Math.Max(picture.Y + 2, frame.Y - label.Height - 12), label.Width + 18, label.Height + 8);
        context.DrawRectangle(Chip, null, chip, 3, 3);
        context.DrawText(label, new Point(chip.X + 9, chip.Y + 4));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Model is null || !Model.ShowOverlay || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetPosition(this);
        var frame = FrameRect(PictureRect());
        _grab = Grab.None;
        foreach (var handle in Handles(frame))
            if (handle.Inflate(5).Contains(p))
                _grab = Grab.Corner;
        if (_grab == Grab.None && frame.Contains(p))
            _grab = Grab.Move;
        if (_grab == Grab.None)
            return;

        _lastPoint = p;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Model is null || !Model.ShowOverlay)
            return;
        var p = e.GetPosition(this);
        var picture = PictureRect();

        if (_grab == Grab.None)
        {
            var frame = FrameRect(picture);
            var overHandle = false;
            foreach (var handle in Handles(frame))
                overHandle |= handle.Inflate(5).Contains(p);
            Cursor = overHandle ? new Cursor(StandardCursorType.SizeAll)
                : frame.Contains(p) ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            return;
        }

        if (_grab == Grab.Move)
        {
            var dx = (p.X - _lastPoint.X) / picture.Width;
            var dy = (p.Y - _lastPoint.Y) / picture.Height;
            Model.SetCamera(Model.CenterX + dx, Model.CenterY + dy, Model.Zoom);
        }
        else
        {
            // keep the picture's aspect: the new size follows whichever axis the pointer is further along
            var nx = (p.X - picture.X) / picture.Width;
            var ny = (p.Y - picture.Y) / picture.Height;
            var size = Math.Max(Math.Abs(nx - Model.CenterX), Math.Abs(ny - Model.CenterY)) * 2;
            Model.SetCamera(Model.CenterX, Model.CenterY, 1.0 / Math.Max(1.0 / 8, size));
        }
        _lastPoint = p;
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
