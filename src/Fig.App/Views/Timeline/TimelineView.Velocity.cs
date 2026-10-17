using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.Services;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>
    /// The speed line of a velocity edit, drawn over a video clip's picture (the Vegas velocity envelope). Up is
    /// faster, down slower, on a log scale with 1× in the middle and 8× and ⅛× at the edges. Drag a point to
    /// reshape the ramp (sideways it snaps to beats and markers), double-click the line to add a point, right-click a
    /// point for its easing or to delete it. The clip keeps its length; the curve decides how much footage it uses.
    /// </summary>
    public partial class TimelineView
    {
        private const double SpeedPointRadius = 4.5;
        private const double SpeedGrabPx = 7;
        private const double SpeedLinePad = 5;
        private static readonly double SpeedScale = Math.Log(8);   // the edges of the band are 8× and 1/8×

        private static readonly IBrush SpeedLineBrush = new SolidColorBrush(Color.Parse("#ffd166"));
        private static readonly Pen SpeedLinePen = new(SpeedLineBrush, 1.8);
        private static readonly Pen SpeedUnityPen = new(new SolidColorBrush(Color.FromArgb(70, 255, 209, 102)), 1, DashStyle.Dash);
        private static readonly IBrush SpeedOutOfFootage = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        private static readonly IBrush SpeedLabelBack = new SolidColorBrush(Color.FromArgb(200, 20, 20, 24));

        /// <summary>A point being dragged: its index in the curve as it was when the drag began.</summary>
        private sealed record SpeedDrag(string ClipId, int Index, List<KeyframePoint> Original, string Gesture);

        private SpeedDrag? _speedDrag;
        private (string ClipId, int Index)? _hoverSpeedPoint;
        private int _speedGestureSeq;

        /// <summary>The band a clip's speed line lives in: its picture, a little inset.</summary>
        private Rect? SpeedBand(Clip clip)
        {
            if (Editor?.Document.Tracks.FirstOrDefault(t => t.Clips.Contains(clip)) is not { } track)
                return null;
            var index = Editor.Document.Tracks.IndexOf(track);
            var top = TrackTop(index) + TimelineGeometry.ClipInset + TimelineGeometry.LabelHeight(track);
            var height = TimelineGeometry.BodyHeight(track) - SoundStripHeight(clip);
            var x = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec);
            return SpeedBand(new Rect(x, top, clip.DurSec * Viewport.PixelsPerSecond, height));
        }

        private static Rect SpeedBand(Rect picture) => picture.Deflate(new Thickness(0, SpeedLinePad));

        private static double SpeedToY(Rect band, double speed)
            => band.Center.Y - Math.Log(Math.Clamp(speed, ClipTime.MinSpeed, ClipTime.MaxSpeed)) / SpeedScale * band.Height / 2;

        private static double YToSpeed(Rect band, double y)
            => Math.Clamp(Math.Exp((band.Center.Y - y) / (band.Height / 2) * SpeedScale), ClipTime.MinSpeed, ClipTime.MaxSpeed);

        /// <summary>Draws the speed line over <paramref name="picture"/>, for a clip with a curve.</summary>
        private void DrawSpeedCurve(DrawingContext context, Clip clip, Rect picture)
        {
            if (ClipTime.Curve(clip) is not { } keys || picture.Width < 2)
                return;
            var band = SpeedBand(picture);
            var (visLeft, visRight) = VisibleXRange(picture);
            if (visRight <= visLeft)
                return;

            using (context.PushClip(picture))
            {
                // a fast curve can use up the file before the clip ends: from there the picture holds its last frame
                if (MediaById is not null && clip.MediaId is { } mediaId && MediaById.TryGetValue(mediaId, out var asset)
                    && asset.DurationSec > 0 && ClipTime.SourceAt(clip, clip.DurSec) > asset.DurationSec + 1e-3)
                {
                    var runsOut = picture.X + ClipTime.LocalAt(clip, asset.DurationSec) * Viewport.PixelsPerSecond;
                    var held = new Rect(runsOut, picture.Y, Math.Max(0, picture.Right - runsOut), picture.Height);
                    context.DrawRectangle(SpeedOutOfFootage, null, held);
                    if (held.Width > 70)
                    {
                        var note = new FormattedText("out of footage", System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White);
                        context.DrawText(note, new Point(held.X + 6, held.Bottom - note.Height - 3));
                    }
                }

                var unity = SpeedToY(band, 1);
                context.DrawLine(SpeedUnityPen, new Point(visLeft, unity), new Point(visRight, unity));

                var line = new StreamGeometry();
                using (var gc = line.Open())
                {
                    var started = false;
                    for (var x = Math.Floor(visLeft); x <= visRight + 2; x += 2)
                    {
                        var t = (x - picture.X) / Viewport.PixelsPerSecond;
                        var p = new Point(x, SpeedToY(band, ClipTime.SpeedAt(clip, t)));
                        if (!started)
                        {
                            gc.BeginFigure(p, false);
                            started = true;
                        }
                        else
                            gc.LineTo(p);
                    }
                    if (started)
                        gc.EndFigure(false);
                }
                context.DrawGeometry(null, SpeedLinePen, line);

                for (var i = 0; i < keys.Count; i++)
                {
                    if (keys[i].TimeSec > clip.DurSec + 1e-6)
                        continue;   // past the end: kept, but hidden until the clip is trimmed longer
                    var center = new Point(picture.X + keys[i].TimeSec * Viewport.PixelsPerSecond, SpeedToY(band, keys[i].Value.AsNumber));
                    var active = _speedDrag is { } d && d.ClipId == clip.Id && d.Index == i
                                 || _hoverSpeedPoint is { } h && h.ClipId == clip.Id && h.Index == i;
                    context.DrawEllipse(active ? Brushes.White : SpeedLineBrush, new Pen(Brushes.Black, 1), center,
                        active ? SpeedPointRadius + 1 : SpeedPointRadius, active ? SpeedPointRadius + 1 : SpeedPointRadius);
                    if (active)
                        DrawSpeedLabel(context, center, keys[i].Value.AsNumber, keys[i].Easing);
                }
            }
        }

        private static void DrawSpeedLabel(DrawingContext context, Point at, double speed, KeyframeEasing easing)
        {
            var text = new FormattedText(easing == KeyframeEasing.Linear ? $"{speed:0.00}×" : $"{speed:0.00}×  {SpeedEasingName(easing)}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White);
            var box = new Rect(at.X + 8, at.Y - text.Height - 6, text.Width + 8, text.Height + 2);
            context.DrawRectangle(SpeedLabelBack, null, new RoundedRect(box, 3));
            context.DrawText(text, new Point(box.X + 4, box.Y + 1));
        }

        private static string SpeedEasingName(KeyframeEasing easing) => easing switch
        {
            KeyframeEasing.EaseIn => "ease in",
            KeyframeEasing.EaseOut => "ease out",
            KeyframeEasing.EaseInOut => "ease in and out",
            KeyframeEasing.Hold => "hold",
            _ => "linear",
        };

        /// <summary>The speed point under <paramref name="pos"/>, on any clip with a curve.</summary>
        private (Clip Clip, int Index)? HitTestSpeedPoint(Point pos)
        {
            if (Editor is null || pos.X <= TrackHeaderWidth)
                return null;
            foreach (var clip in Editor.Document.Tracks.SelectMany(t => t.Clips))
            {
                if (ClipTime.Curve(clip) is not { } keys || SpeedBand(clip) is not { } band)
                    continue;
                if (pos.Y < band.Top - SpeedGrabPx || pos.Y > band.Bottom + SpeedGrabPx)
                    continue;
                for (var i = 0; i < keys.Count; i++)
                {
                    if (keys[i].TimeSec > clip.DurSec + 1e-6)
                        continue;
                    var x = band.X + keys[i].TimeSec * Viewport.PixelsPerSecond;
                    var y = SpeedToY(band, keys[i].Value.AsNumber);
                    if (Math.Abs(pos.X - x) <= SpeedGrabPx && Math.Abs(pos.Y - y) <= SpeedGrabPx)
                        return (clip, i);
                }
            }
            return null;
        }

        /// <summary>The clip whose speed line passes near <paramref name="pos"/>.</summary>
        private Clip? HitTestSpeedLine(Point pos)
        {
            if (Editor is null || pos.X <= TrackHeaderWidth)
                return null;
            foreach (var clip in Editor.Document.Tracks.SelectMany(t => t.Clips))
            {
                if (!ClipTime.HasCurve(clip) || SpeedBand(clip) is not { } band
                    || pos.X < band.X || pos.X > band.Right || pos.Y < band.Top - SpeedGrabPx || pos.Y > band.Bottom + SpeedGrabPx)
                    continue;
                var t = (pos.X - band.X) / Viewport.PixelsPerSecond;
                if (Math.Abs(pos.Y - SpeedToY(band, ClipTime.SpeedAt(clip, t))) <= SpeedGrabPx)
                    return clip;
            }
            return null;
        }

        /// <summary>
        /// A left press on the speed line: grabs a point, or with a double-click adds one where the line is.
        /// True when it was handled.
        /// </summary>
        private bool HandleSpeedPress(PointerPressedEventArgs e, Point pos)
        {
            if (Editor is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return false;
            if (HitTestSpeedPoint(pos) is { } hit)
            {
                if (e.ClickCount >= 2)
                {
                    // double-click a point: back to 1×
                    var reset = ClipTime.Curve(hit.Clip)!.ToList();
                    reset[hit.Index] = reset[hit.Index] with { Value = ParamValue.OfDouble(1) };
                    Editor.SetSpeedCurve(hit.Clip.Id, reset, "Reset speed point");
                    InvalidateVisual();
                    return true;
                }
                _speedDrag = new SpeedDrag(hit.Clip.Id, hit.Index, ClipTime.Curve(hit.Clip)!.ToList(), $"speed-{++_speedGestureSeq}");
                e.Pointer.Capture(this);
                InvalidateVisual();
                return true;
            }
            if (e.ClickCount >= 2 && HitTestSpeedLine(pos) is { } clip)
            {
                var t = Math.Clamp(Editor.SnapTime((pos.X - TrackHeaderWidth - Viewport.TimeToX(clip.StartSec)) / Viewport.PixelsPerSecond), 0, clip.DurSec);
                var keys = ClipTime.Curve(clip)!.ToList();
                keys.Add(new KeyframePoint(t, ParamValue.OfDouble(ClipTime.SpeedAt(clip, t)), EasingNear(keys, t)));
                Editor.SetSpeedCurve(clip.Id, keys, "Add speed point");
                InvalidateVisual();
                return true;
            }
            return false;
        }

        /// <summary>A new point carries on the easing of the segment it lands in.</summary>
        private static KeyframeEasing EasingNear(List<KeyframePoint> keys, double t)
            => keys.Where(k => k.TimeSec <= t).Select(k => k.Easing).DefaultIfEmpty(KeyframeEasing.Linear).Last();

        /// <summary>Moves the dragged point: up and down changes its speed, sideways its time (between its neighbours).</summary>
        private void UpdateSpeedDrag(Point pos)
        {
            if (_speedDrag is not { } drag || Editor?.FindClipForView(drag.ClipId) is not { } clip || SpeedBand(clip) is not { } band)
                return;
            var keys = drag.Original.ToList();
            var point = keys[drag.Index];

            var speed = YToSpeed(band, pos.Y);
            if (Math.Abs(Math.Log(speed)) < 0.06)
                speed = 1;   // a little magnet at 1×

            var lo = drag.Index > 0 ? keys[drag.Index - 1].TimeSec + 0.02 : 0;
            var hi = drag.Index < keys.Count - 1 ? keys[drag.Index + 1].TimeSec - 0.02 : clip.DurSec;
            var time = point.TimeSec;
            if (hi > lo)
            {
                var raw = XToTime(pos.X);
                time = Math.Clamp(Snap(raw, clip.Id) - clip.StartSec, lo, hi);
            }

            keys[drag.Index] = point with { TimeSec = time, Value = ParamValue.OfDouble(speed) };
            Editor.SetSpeedCurve(clip.Id, keys, "Move speed point", drag.Gesture);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            InvalidateVisual();
        }

        private bool EndSpeedDrag(PointerReleasedEventArgs e)
        {
            if (_speedDrag is null)
                return false;
            _speedDrag = null;
            if (e.Pointer.Captured == this)
                e.Pointer.Capture(null);
            ClearSnapGuide();
            Cursor = Cursor.Default;
            InvalidateVisual();
            return true;
        }

        /// <summary>Hover over a speed point: highlight it and show its speed. True when over one.</summary>
        private bool UpdateSpeedHover(Point pos)
        {
            var hit = HitTestSpeedPoint(pos);
            (string, int)? over = hit is { } h ? (h.Clip.Id, h.Index) : null;
            if (over != _hoverSpeedPoint)
            {
                _hoverSpeedPoint = over;
                InvalidateVisual();
            }
            if (hit is null)
                return false;
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, "Drag up for faster, down for slower · double-click for 1× · right-click for easing");
            return true;
        }

        /// <summary>Right-click on a speed point: its easing, and delete. True when a point was there.</summary>
        private bool TryOpenSpeedPointMenu(Point pos)
        {
            if (Editor is null || HitTestSpeedPoint(pos) is not { } hit || ClipTime.Curve(hit.Clip) is not { } keys)
                return false;
            var menu = new ContextMenu();
            var current = keys[hit.Index].Easing;
            foreach (var easing in new[] { KeyframeEasing.Linear, KeyframeEasing.EaseIn, KeyframeEasing.EaseOut, KeyframeEasing.EaseInOut, KeyframeEasing.Hold })
            {
                var chosen = easing;
                var name = char.ToUpper(SpeedEasingName(easing)[0]) + SpeedEasingName(easing)[1..];
                menu.Items.Add(Item((chosen == current ? "✓ " : "   ") + name, () =>
                {
                    var changed = ClipTime.Curve(hit.Clip)!.ToList();
                    changed[hit.Index] = changed[hit.Index] with { Easing = chosen };
                    Editor.SetSpeedCurve(hit.Clip.Id, changed, "Change speed easing");
                    InvalidateVisual();
                }, hint: "How the speed moves from this point to the next"));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Delete point", () =>
            {
                var changed = ClipTime.Curve(hit.Clip)!.ToList();
                changed.RemoveAt(hit.Index);
                if (changed.Count == 0)
                    Editor.RemoveSpeedCurve(hit.Clip.Id);
                else
                    Editor.SetSpeedCurve(hit.Clip.Id, changed, "Delete speed point");
                InvalidateVisual();
            }, danger: true));
            menu.Open(this);
            return true;
        }
    }
}
