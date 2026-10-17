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
/// A keyframe host's keys on a ruler that spans what it covers: big diamonds to pick and drag, the playhead, and
/// (optionally) the stretch an effect is limited to. Click empty space to move the playhead, double-click to add a
/// keyframe there, right-click a diamond for the same menu every keyframe has (<see cref="KeyframeMenu"/>).
/// Works for any kind of keyframe: give it a <see cref="KeyframeLaneViewModel"/> through <see cref="Lane"/>.
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

    public static readonly StyledProperty<KeyframeLaneViewModel?> LaneProperty =
        AvaloniaProperty.Register<KeyframeRuler, KeyframeLaneViewModel?>(nameof(Lane));

    /// <summary>The keyframes shown and edited.</summary>
    public KeyframeLaneViewModel? Lane
    {
        get => GetValue(LaneProperty);
        set => SetValue(LaneProperty, value);
    }

    private KeyframeLaneViewModel? _vm;
    private double? _dragKey;
    private string _gesture = "";
    private bool _scrubbing;
    private double? _hover;

    public KeyframeRuler()
    {
        ClipToBounds = true;
        Height = 66;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LaneProperty)
            return;
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmChanged;
        _vm = Lane;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmChanged;
        InvalidateVisual();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private Rect LaneRect => new(Pad, LabelBand, Math.Max(1, Bounds.Width - 2 * Pad), Bounds.Height - LabelBand - 6);

    private double XOf(double sec) => LaneRect.X + Math.Clamp(sec / Math.Max(0.001, _vm!.ClipDurationSec), 0, 1) * LaneRect.Width;

    private double TimeOf(double x) => Math.Clamp((x - LaneRect.X) / LaneRect.Width, 0, 1) * _vm!.ClipDurationSec;

    private double? KeyAt(Point p)
    {
        if (_vm is null)
            return null;
        double? best = null;
        var bestDistance = HitRadius;
        foreach (var k in _vm.KeyTimes)
        {
            var d = Math.Abs(XOf(k) - p.X);
            if (d <= bestDistance && Math.Abs(p.Y - LaneRect.Center.Y) <= 14)
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

        var lane = LaneRect;
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
            _vm.Select(hit);
            _dragKey = hit;
            _gesture = Guid.NewGuid().ToString("N");
        }
        else if (e.ClickCount >= 2)
        {
            _vm.Add(_vm.Snap(TimeOf(p.X)));
        }
        else
        {
            _scrubbing = true;
            _vm.Seek(_vm.Snap(TimeOf(p.X)));
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
            var target = Math.Clamp(_vm.Snap(TimeOf(p.X)), 0, _vm.ClipDurationSec);
            if (Math.Abs(target - current) > 1e-6)
            {
                _vm.Move(current, target, _gesture);
                _dragKey = target;
            }
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            _vm.Seek(_vm.Snap(TimeOf(p.X)));
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
        if (_vm?.Host is not { } host)
            return;
        KeyframeMenu.Build(_vm.Editor, new KeyframeRef(host, time), InvalidateVisual).Open(this);
    }
}
