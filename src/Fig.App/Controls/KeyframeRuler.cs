using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.ViewModels;
using Fig.Core.Timeline;

namespace Fig.App.Controls;

/// <summary>
/// The selected effect's keyframes on a ruler that spans the whole clip: big diamonds to select and
/// drag, the playhead, and (optionally) the stretch the effect is limited to. Click empty space to move
/// the playhead, double-click to add a keyframe there, right-click a diamond for more.
/// </summary>
public sealed class KeyframeRuler : Control
{
    private const double Pad = 12;
    private const double LabelBand = 18;
    private const double HitRadius = 10;

    private static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#131415"));
    private static readonly IBrush LimitBand = new SolidColorBrush(Color.FromArgb(45, 0x5f, 0x9c, 0xf8));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#5f9cf8"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8a8a8a"));
    private static readonly IBrush Playhead = new SolidColorBrush(Color.Parse("#ff6b5e"));
    private static readonly Pen Border = new(new SolidColorBrush(Color.Parse("#2b2d31")), 1);
    private static readonly Pen Tick = new(new SolidColorBrush(Color.Parse("#3a3c42")), 1);

    private TransformControlsViewModel? _vm;
    private double? _dragKey;
    private string _gesture = "";
    private bool _scrubbing;
    private double? _hover;

    public KeyframeRuler()
    {
        ClipToBounds = true;
        Height = 66;
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as TransformControlsViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
            InvalidateVisual();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private Rect Lane => new(Pad, LabelBand, Math.Max(1, Bounds.Width - 2 * Pad), Bounds.Height - LabelBand - 6);

    private double XOf(double sec) => Lane.X + Math.Clamp(sec / Math.Max(0.001, _vm!.ClipDurationSec), 0, 1) * Lane.Width;

    /// <summary>Test hook: where a clip-relative time is on the ruler (control coordinates).</summary>
    internal Point PointForTime(double sec) => new(XOf(sec), Lane.Center.Y - 2);

    private double TimeOf(double x) => Math.Clamp((x - Lane.X) / Lane.Width, 0, 1) * _vm!.ClipDurationSec;

    private double? KeyAt(Point p)
    {
        if (_vm is null)
            return null;
        double? best = null;
        var bestDistance = HitRadius;
        foreach (var k in _vm.KeyTimes)
        {
            var d = Math.Abs(XOf(k) - p.X);
            if (d <= bestDistance && Math.Abs(p.Y - Lane.Center.Y) <= 14)
            {
                best = k;
                bestDistance = d;
            }
        }
        return best;
    }

    private static string Clock(double t) => $"{(int)(t / 60):00}:{t % 60:00.0}";

    public override void Render(DrawingContext context)
    {
        // transparent fill so the whole ruler receives pointer events
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (_vm is null)
            return;

        var lane = Lane;
        context.DrawRectangle(Surface, Border, lane, 5, 5);

        if (_vm.LimitStart is { } ls && _vm.LimitEnd is { } le)
            context.DrawRectangle(LimitBand, null, new Rect(XOf(ls), lane.Y + 1, Math.Max(2, XOf(le) - XOf(ls)), lane.Height - 2), 4, 4);

        // time ticks and labels, spaced so they never crowd
        var steps = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 };
        var step = steps.FirstOrDefault(s => s / _vm.ClipDurationSec * lane.Width >= 56, steps[^1]);
        for (var t = 0.0; t <= _vm.ClipDurationSec + 1e-6; t += step)
        {
            var x = XOf(t);
            context.DrawLine(Tick, new Point(x, lane.Y + lane.Height - 7), new Point(x, lane.Y + lane.Height - 1));
            var label = new FormattedText(Clock(t), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 9, Muted);
            if (x + label.Width < Bounds.Width)
                context.DrawText(label, new Point(x, 2));
        }

        var keys = _vm.KeyTimes;
        var cy = lane.Center.Y - 2;
        if (keys.Count > 1)
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 0x5f, 0x9c, 0xf8)), 1.5), new Point(XOf(keys[0]), cy), new Point(XOf(keys[^1]), cy));

        // a lone keyframe (or the ends of a longer animation) holds its value outward: show it
        if (keys.Count > 0)
        {
            var hold = new Pen(new SolidColorBrush(Color.FromArgb(90, 0x5f, 0x9c, 0xf8)), 1.5, new DashStyle(new double[] { 3, 3 }, 0));
            context.DrawLine(hold, new Point(lane.X + 3, cy), new Point(XOf(keys[0]), cy));
            context.DrawLine(hold, new Point(XOf(keys[^1]), cy), new Point(lane.Right - 3, cy));
        }

        foreach (var k in keys)
        {
            var selected = _vm.SelectedKeyframe is { } sel && Math.Abs(sel - k) < 1e-4;
            var hot = _hover is { } h && Math.Abs(h - k) < 1e-6;
            var size = selected || hot ? 8.0 : 7.0;
            var x = XOf(k);
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(x, cy - size), true);
                gc.LineTo(new Point(x + size, cy));
                gc.LineTo(new Point(x, cy + size));
                gc.LineTo(new Point(x - size, cy));
                gc.EndFigure(true);
            }
            context.DrawGeometry(selected ? Brushes.White : Accent, new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 1.2), g);
        }

        // the playhead
        var px = XOf(_vm.PlayheadLocal);
        context.DrawLine(new Pen(Playhead, 1.5), new Point(px, lane.Y - 2), new Point(px, lane.Bottom));
        var cap = new StreamGeometry();
        using (var gc = cap.Open())
        {
            gc.BeginFigure(new Point(px - 4, lane.Y - 4), true);
            gc.LineTo(new Point(px + 4, lane.Y - 4));
            gc.LineTo(new Point(px, lane.Y + 2));
            gc.EndFigure(true);
        }
        context.DrawGeometry(Playhead, null, cap);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null)
            return;
        var p = e.GetPosition(this);
        var key = KeyAt(p);
        var point = e.GetCurrentPoint(this).Properties;

        if (point.IsRightButtonPressed)
        {
            if (key is { } k)
                OpenKeyMenu(k);
            e.Handled = true;
            return;
        }
        if (!point.IsLeftButtonPressed)
            return;

        if (key is { } hit)
        {
            _vm.RulerSelect(hit);
            _dragKey = hit;
            _gesture = Guid.NewGuid().ToString("N");
        }
        else if (e.ClickCount >= 2)
        {
            _vm.RulerAdd(_vm.RulerSnap(TimeOf(p.X)));
        }
        else
        {
            _scrubbing = true;
            _vm.RulerSeek(_vm.RulerSnap(TimeOf(p.X)));
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null)
            return;
        var p = e.GetPosition(this);

        if (_dragKey is { } current)
        {
            var target = Math.Clamp(_vm.RulerSnap(TimeOf(p.X)), 0, _vm.ClipDurationSec);
            if (Math.Abs(target - current) > 1e-6)
            {
                _vm.RulerMove(current, target, _gesture);
                _dragKey = target;
            }
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            _vm.RulerSeek(_vm.RulerSnap(TimeOf(p.X)));
            e.Handled = true;
            return;
        }

        var over = KeyAt(p);
        if (over != _hover)
        {
            _hover = over;
            ToolTip.SetTip(this, over is { } k ? $"Keyframe at {Clock(k)} · drag to retime · right-click for more" : null);
            InvalidateVisual();
        }
        Cursor = over is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if ((_dragKey is not null || _scrubbing) && e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        _dragKey = null;
        _scrubbing = false;
    }

    private void OpenKeyMenu(double time)
    {
        if (_vm is null)
            return;
        var menu = new ContextMenu();

        var jump = new MenuItem { Header = "Jump to keyframe" };
        jump.Click += (_, _) => _vm.RulerSelect(time);
        menu.Items.Add(jump);

        var duplicate = new MenuItem { Header = "Duplicate to the playhead" };
        duplicate.Click += (_, _) => _vm.RulerDuplicateToPlayhead(time);
        menu.Items.Add(duplicate);

        var current = _vm.RulerEasingAt(time);
        var easing = new MenuItem { Header = "Movement to the next keyframe" };
        foreach (var option in Enum.GetValues<KeyframeEasing>())
        {
            var item = new MenuItem { Header = (option == current ? "✓  " : "     ") + TransformControlsViewModel.EasingNames[(int)option] };
            item.Click += (_, _) => _vm.RulerSetEasing(time, option);
            easing.Items.Add(item);
        }
        menu.Items.Add(easing);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Delete keyframe", Foreground = new SolidColorBrush(Color.Parse("#ff6b6b")) };
        delete.Click += (_, _) => _vm.RulerDelete(time);
        menu.Items.Add(delete);
        menu.Open(this);
    }
}
