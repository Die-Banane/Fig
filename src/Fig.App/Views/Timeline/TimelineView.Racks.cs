using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.Services;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>
    /// The clip "rack": every effect on a clip as a bar under it that keeps its timing. Bars can
    /// be collapsed to thin colored strips or expanded to labelled rows with power / remove
    /// buttons, and an effect's bar can be dragged to move it or resized to change how much of
    /// the clip it covers.
    /// </summary>
    public partial class TimelineView
    {
        private const double RackPad = 4;
        private const double RackBarCompact = 6;
        private const double RackBarFull = 32;

        /// <summary>Height of the label row in an open bar; below it is the keyframe lane.</summary>
        private const double RackLabelRow = 20;
        private const double RackGapCompact = 2;
        private const double RackGapFull = 3;
        private const double MinRackBarWidth = 4;

        // racks start open so the effects on a clip are always visible; folding is per track (one toggle in
        // the track header), never per clip
        private readonly HashSet<string> _collapsedTrackRacks = new();

        private static readonly Color EffectFill = Color.Parse("#6f5526");
        private static readonly Color EffectEdge = Color.Parse("#c79a3d");
        private static readonly Color ZoomFill = Color.Parse("#1f3a66");
        private static readonly Color ZoomEdge = Color.Parse("#5f9cf8");
        private static readonly Color LevelFill = Color.Parse("#2d674a");
        private static readonly Color LevelEdge = Color.Parse("#589b74");

        private sealed record RackItem(string Key, string Label, Color Fill, Color Edge,
            EffectInstance? Effect, double StartSec, double DurSec, bool Enabled, bool Ranged, int Tab);

        private enum RackPart { None, Body, Left, Right, Power, Remove, Keyframe, Animate }

        /// <param name="KeyframeSec">For <see cref="RackPart.Keyframe"/>: its clip-relative time.</param>
        /// <param name="InKeyLane">The pointer is in the bar's keyframe lane (the strip under the label).</param>
        private readonly record struct RackHit(Clip Clip, RackItem? Item, RackPart Part, Rect Rect,
            double KeyframeSec = 0, bool InKeyLane = false);

        private sealed record KeyDrag(string ClipId, string EffectId, double CurrentSec, string Gesture);

        private KeyDrag? _keyDrag;

        /// <summary>Clip-relative times where an effect has a keyframe, in order.</summary>
        private static List<double> KeyframeTimes(EffectInstance fx)
            => fx.Keyframes.Values.SelectMany(t => t.Select(k => Math.Round(k.TimeSec, 6))).Distinct().OrderBy(t => t).ToList();

        private sealed record RackDrag(string ClipId, string EffectId, RackPart Part, double OrigStart, double OrigDur, double PressTime);

        private RackDrag? _rackDrag;

        private static List<RackItem> BuildRack(Clip clip)
        {
            var items = new List<RackItem>();
            foreach (var fx in clip.Effects.OrderBy(e => e.Order))
            {
                var entry = EffectCatalog.Find(fx.TypeId);
                var motion = fx.TypeId == EffectCatalog.Transform;
                var start = fx.HasRange ? fx.StartSec!.Value : 0;
                var dur = fx.HasRange ? fx.DurSec!.Value : clip.DurSec;
                items.Add(new RackItem(fx.Id, entry?.DisplayName ?? fx.TypeId,
                    motion ? ZoomFill : EffectFill, motion ? ZoomEdge : EffectEdge,
                    fx, start, dur, fx.Enabled, motion, 4));
            }

            if (clip is AudioClip)
            {
                if (Math.Abs(clip.Volume - 1) > 0.01 || clip.Keyframes.ContainsKey(AutomationKeys.Volume))
                    items.Add(new RackItem("level", "Level", LevelFill, LevelEdge, null, 0, clip.DurSec, true, false, 1));
                if (clip.FadeInSec > 1e-3)
                    items.Add(new RackItem("fade-in", "Fade in", ZoomFill, ZoomEdge, null, 0, clip.FadeInSec, true, false, 3));
                if (clip.FadeOutSec > 1e-3)
                    items.Add(new RackItem("fade-out", "Fade out", ZoomFill, ZoomEdge, null,
                        Math.Max(0, clip.DurSec - clip.FadeOutSec), clip.FadeOutSec, true, false, 3));
            }
            return items;
        }

        /// <summary>Shows or hides the labelled effect rows for every clip on a track.</summary>
        public void SetTrackRackExpanded(string trackId, bool expanded)
        {
            if (expanded ? _collapsedTrackRacks.Remove(trackId) : _collapsedTrackRacks.Add(trackId))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        private bool IsTrackRackExpanded(Track track) => !_collapsedTrackRacks.Contains(track.Id);

        /// <summary>True when any clip on the track has effect bars (so the fold toggle is worth showing).</summary>
        private bool TrackHasRack(Track track) => track.Clips.Any(c => BuildRack(c).Count > 0);

        /// <summary>The fold toggle: on the right of the track's header, level with the clips' label strip.</summary>
        private Rect RackToggleRect(Track track)
        {
            var index = Editor?.Document.Tracks.IndexOf(track) ?? track.Index;
            return new Rect(TrackHeaderWidth - 8 - 20, TrackTop(index) + TimelineGeometry.ClipInset + 1, 20,
                TimelineGeometry.ClipLabelHeight - 2);
        }

        private void DrawRackToggle(DrawingContext context, Track track)
        {
            if (!TrackHasRack(track))
                return;
            var rect = RackToggleRect(track);
            var hovered = _hoverHeaderButton is (string tid, int col) && tid == track.Id && col == HeaderRackColumn;
            context.DrawRectangle(hovered ? DeleteHoverBrush : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                null, rect, 3, 3);
            IconService.DrawStroked(context, IsTrackRackExpanded(track) ? "chevron-down" : "chevron-right",
                new Rect(rect.X + 4, rect.Y, 12, rect.Height), TrackHeaderTextBrush, 1.8);
        }

        /// <summary>Which track a clip sits on (clips don't know).</summary>
        private Track? TrackOf(Clip clip) =>
            Editor?.Document.Tracks.FirstOrDefault(t => t.Clips.Any(c => ReferenceEquals(c, clip)));

        /// <summary>
        /// Test hook: where an effect bar (or one of its keyframes) is, in this control's coordinates.
        /// For a keyframe the point is on its diamond; otherwise it is the middle of the bar's key lane.
        /// </summary>
        internal Point? RackPointFor(string clipId, string effectId, double? keyframeSec = null)
        {
            if (Editor is null)
                return null;
            for (var t = 0; t < Editor.Document.Tracks.Count; t++)
            {
                var track = Editor.Document.Tracks[t];
                var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
                if (clip is null)
                    continue;
                var items = BuildRack(clip);
                var index = items.FindIndex(i => i.Effect?.Id == effectId);
                if (index < 0)
                    return null;
                var clipX = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec);
                var rect = RackItemRect(clip, items[index], index, clipX, TrackTop(t) + TimelineGeometry.ClipInset, track.Kind);
                var y = rect.Y + RackLabelRow + (RackBarFull - RackLabelRow) / 2.0;
                var x = keyframeSec is { } k ? clipX + Viewport.TimeToX(k) - Viewport.TimeToX(0) : rect.Center.X;
                return new Point(keyframeSec is null ? rect.Center.X : TrackHeaderWidth + Viewport.TimeToX(clip.StartSec + keyframeSec.Value), y);
            }
            return null;
        }

        private bool IsRackExpanded(Clip clip) => TrackOf(clip) is not { } track || IsTrackRackExpanded(track);

        private double RackBar(Clip clip) => IsRackExpanded(clip) ? RackBarFull : RackBarCompact;

        private double RackGap(Clip clip) => IsRackExpanded(clip) ? RackGapFull : RackGapCompact;

        /// <summary>Extra lane height a clip needs for its rack (0 when it has nothing to show).</summary>
        private double RackHeight(Clip clip)
        {
            var n = BuildRack(clip).Count;
            return n == 0 ? 0 : RackPad + n * (RackBar(clip) + RackGap(clip));
        }

        private double TrackRackHeight(Track track) => track.Clips.Count == 0 ? 0 : track.Clips.Max(RackHeight);

        private Rect RackItemRect(Clip clip, RackItem item, int index, double clipX, double clipTop, TrackKind kind)
        {
            var px = Viewport.PixelsPerSecond;
            var top = clipTop + TimelineGeometry.ClipTotal(kind) + RackPad + index * (RackBar(clip) + RackGap(clip));
            return new Rect(clipX + item.StartSec * px, top, Math.Max(MinRackBarWidth, item.DurSec * px), RackBar(clip));
        }

        private void DrawRack(DrawingContext context, Clip clip, double clipX, double clipW, double clipTop, TrackKind kind)
        {
            var items = BuildRack(clip);
            if (items.Count == 0)
                return;

            var expanded = IsRackExpanded(clip);
            var selectedEffectId = _editorVm?.Properties.SelectedEffect?.EffectId;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rect = RackItemRect(clip, item, i, clipX, clipTop, kind);
                var alpha = item.Enabled ? (byte)255 : (byte)110;
                var fill = new SolidColorBrush(Color.FromArgb(alpha, item.Fill.R, item.Fill.G, item.Fill.B));
                var selected = item.Effect is not null && item.Effect.Id == selectedEffectId;
                var outline = selected
                    ? new Pen(Brushes.White, 1.5)
                    : new Pen(new SolidColorBrush(Color.FromArgb(alpha, item.Edge.R, item.Edge.G, item.Edge.B)), 1);
                context.DrawRectangle(fill, outline, rect, 3, 3);

                DrawBarKeyframes(context, clip, item, rect, expanded);

                if (!expanded)
                    continue;

                using (context.PushClip(rect.Deflate(new Thickness(2, 0))))
                {
                    var buttons = rect.Width >= 110;
                    var textRect = new Rect(rect.X + 8, rect.Y, Math.Max(0, rect.Width - (buttons ? 72 : 14)), RackLabelRow);
                    var label = new FormattedText(item.Label, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White)
                    {
                        MaxTextWidth = Math.Max(1, textRect.Width),
                        MaxTextHeight = RackLabelRow,
                        Trimming = TextTrimming.CharacterEllipsis,
                    };
                    context.DrawText(label, new Point(textRect.X, rect.Y + (RackLabelRow - label.Height) / 2));

                    if (buttons && item.Effect is not null)
                    {
                        // the stopwatch: lit while the effect is animated
                        var animated = item.Effect.Keyframes.Count > 0;
                        var animate = AnimateRect(rect);
                        if (animated)
                            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), null, animate.Inflate(1), 3, 3);
                        IconService.DrawStroked(context, "timer", animate.Deflate(2),
                            animated ? Brushes.White : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1.6);

                        var (power, remove) = RackButtonRects(rect);
                        IconService.DrawStroked(context, "power", power.Deflate(2), Brushes.White, 1.6);
                        IconService.DrawStroked(context, "x", remove.Deflate(2), Brushes.White, 1.6);
                    }
                }
            }
        }

        private static (Rect Power, Rect Remove) RackButtonRects(Rect bar)
            => (new Rect(bar.Right - 40, bar.Y + 3, 16, 16), new Rect(bar.Right - 22, bar.Y + 3, 16, 16));

        /// <summary>The stopwatch: turns animating (keyframes) on or off for the effect.</summary>
        private static Rect AnimateRect(Rect bar) => new(bar.Right - 60, bar.Y + 3, 16, 16);

        /// <summary>
        /// Draws an effect's keyframes on its bar: diamonds on the key lane of an open bar (joined
        /// by a line, dashed where a segment holds) or small ticks on a folded one.
        /// </summary>
        private void DrawBarKeyframes(DrawingContext context, Clip clip, RackItem item, Rect rect, bool expanded)
        {
            if (item.Effect is not { } fx || fx.Keyframes.Count == 0)
                return;
            var times = KeyframeTimes(fx);
            var px = Viewport.PixelsPerSecond;
            var clipX = rect.X - item.StartSec * px;
            var y = expanded ? rect.Y + RackLabelRow + (RackBarFull - RackLabelRow) / 2.0 : rect.Center.Y;
            var edge = new SolidColorBrush(item.Edge);
            var selectedId = _editorVm?.Properties.SelectedEffect?.EffectId;
            var selectedSec = _editorVm?.Properties.SelectedKeyframeSec;

            if (expanded && times.Count > 1)
            {
                var first = clipX + times[0] * px;
                var last = clipX + times[^1] * px;
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(130, item.Edge.R, item.Edge.G, item.Edge.B)), 1.2), new Point(first, y), new Point(last, y));
            }

            foreach (var time in times)
            {
                var x = clipX + time * px;
                if (x < rect.X - 6 || x > rect.Right + 6)
                    continue;
                var selected = fx.Id == selectedId && selectedSec is { } sel && Math.Abs(sel - time) < 1e-4;
                var size = expanded ? 5.0 : 3.0;
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(x, y - size), true);
                    gc.LineTo(new Point(x + size, y));
                    gc.LineTo(new Point(x, y + size));
                    gc.LineTo(new Point(x - size, y));
                    gc.EndFigure(true);
                }
                context.DrawGeometry(selected ? Brushes.White : edge, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1), g);
            }
        }

        private RackHit? HitTestRack(Point pos)
        {
            if (Editor is null || pos.X <= TrackHeaderWidth)
                return null;

            for (var t = 0; t < Editor.Document.Tracks.Count; t++)
            {
                var track = Editor.Document.Tracks[t];
                var top = TrackTop(t);
                if (pos.Y < top || pos.Y >= top + TrackHeight(track))
                    continue;
                var clipTop = top + TimelineGeometry.ClipInset;

                foreach (var clip in track.Clips)
                {
                    var items = BuildRack(clip);
                    if (items.Count == 0)
                        continue;
                    var clipX = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec + GetRippleOffsetSec(clip.Id));
                    var clipW = clip.DurSec * Viewport.PixelsPerSecond;

                    for (var i = 0; i < items.Count; i++)
                    {
                        var rect = RackItemRect(clip, items[i], i, clipX, clipTop, track.Kind);
                        // thin bars get a taller grab area, and the ends get a resize zone
                        var grab = rect.Inflate(new Thickness(4, 2));
                        if (!grab.Contains(pos))
                            continue;

                        var expanded = IsRackExpanded(clip);
                        var inKeyLane = expanded && items[i].Effect is not null && pos.Y >= rect.Y + RackLabelRow;

                        if (expanded && rect.Width >= 110 && items[i].Effect is not null && !inKeyLane)
                        {
                            if (AnimateRect(rect).Contains(pos)) return new RackHit(clip, items[i], RackPart.Animate, rect);
                            var (power, remove) = RackButtonRects(rect);
                            if (power.Contains(pos)) return new RackHit(clip, items[i], RackPart.Power, rect);
                            if (remove.Contains(pos)) return new RackHit(clip, items[i], RackPart.Remove, rect);
                        }

                        // keyframe diamonds live in the key lane, away from the resize edges above
                        if (inKeyLane)
                            foreach (var time in KeyframeTimes(items[i].Effect!))
                                if (Math.Abs(pos.X - (clipX + time * Viewport.PixelsPerSecond)) <= 8)
                                    return new RackHit(clip, items[i], RackPart.Keyframe, rect, time, true);

                        var part = RackPart.Body;
                        if (items[i].Effect is not null && !inKeyLane)
                        {
                            var edge = Math.Min(8, Math.Max(3, rect.Width / 3));
                            if (Math.Abs(pos.X - rect.X) <= edge) part = RackPart.Left;
                            else if (Math.Abs(pos.X - rect.Right) <= edge) part = RackPart.Right;
                        }
                        return new RackHit(clip, items[i], part, rect, 0, inKeyLane);
                    }
                }
            }
            return null;
        }

        /// <summary>Handles a left press on the rack. Returns true when it consumed the click.</summary>
        private bool HandleRackPress(RackHit hit, Point pos, int clickCount)
        {
            if (Editor is null)
                return false;

            var item = hit.Item!;
            _selectedClipId = hit.Clip.Id;
            _selectedTrackId = null;

            if (item.Effect is null)
            {
                // audio level / fades: select the clip and open the matching inspector tab
                Editor.Selection.SelectOnly(hit.Clip.Id);
                if (_editorVm is not null)
                    _editorVm.Properties.SelectedTabIndex = item.Tab;
                InvalidateVisual();
                return true;
            }

            if (hit.Part == RackPart.Animate)
            {
                var local = Math.Max(0, (_editorVm?.PlayheadTimeSec ?? 0) - hit.Clip.StartSec);
                Editor.SetEffectAnimated(hit.Clip.Id, item.Effect.Id, item.Effect.Keyframes.Count == 0, local);
                _editorVm?.Properties.SelectEffect(hit.Clip.Id, item.Effect.Id);
                InvalidateVisual();
                return true;
            }

            if (hit.Part == RackPart.Power)
            {
                Editor.ToggleEffect(hit.Clip.Id, item.Effect.Id);
                InvalidateVisual();
                return true;
            }
            if (hit.Part == RackPart.Remove)
            {
                Editor.RemoveEffect(hit.Clip.Id, item.Effect.Id);
                InvalidateMeasure();
                InvalidateVisual();
                return true;
            }

            _editorVm?.Properties.SelectEffect(hit.Clip.Id, item.Effect.Id);

            if (hit.Part == RackPart.Keyframe)
            {
                // click a keyframe to select it and jump there; drag it to retime it
                _editorVm?.Properties.SelectKeyframe(hit.KeyframeSec);
                SetPlayhead(Editor.SnapTime(hit.Clip.StartSec + hit.KeyframeSec));
                _keyDrag = new KeyDrag(hit.Clip.Id, item.Effect.Id, hit.KeyframeSec, Guid.NewGuid().ToString("N"));
                InvalidateVisual();
                return true;
            }

            if (hit.InKeyLane && clickCount >= 2)
            {
                // double-click empty key lane: add a keyframe holding the current look at that time
                var local = Math.Max(0, XToTime(pos.X) - hit.Clip.StartSec);
                if (Editor.AddEffectKeyframe(hit.Clip.Id, item.Effect.Id, local) is { } added)
                    _editorVm?.Properties.SelectKeyframe(added);
                InvalidateVisual();
                return true;
            }

            // a bar covering the whole clip has nothing to move; only its ends can be dragged, to limit the effect
            if (hit.Part == RackPart.Body && !item.Effect.HasRange)
            {
                InvalidateVisual();
                return true;
            }

            _rackDrag = new RackDrag(hit.Clip.Id, item.Effect.Id, hit.Part,
                item.StartSec, item.DurSec, XToTime(pos.X));
            InvalidateVisual();
            return true;
        }

        private void UpdateRackDrag(Point pos)
        {
            if (Editor is null || _rackDrag is not { } drag || FindClipById(drag.ClipId) is not { } clip)
                return;

            const double minLength = 0.1;
            var local = Snap(Math.Max(0, XToTime(pos.X)), includePlayhead: false) - clip.StartSec;
            double start, dur;
            switch (drag.Part)
            {
                case RackPart.Left:
                {
                    var end = drag.OrigStart + drag.OrigDur;
                    start = Math.Clamp(local, 0, end - minLength);
                    dur = end - start;
                    break;
                }
                case RackPart.Right:
                    start = drag.OrigStart;
                    dur = Math.Clamp(local - drag.OrigStart, minLength, clip.DurSec - drag.OrigStart);
                    break;
                default:
                {
                    var moved = drag.OrigStart + (XToTime(pos.X) - drag.PressTime);
                    start = Math.Clamp(Snap(clip.StartSec + moved) - clip.StartSec, 0, Math.Max(0, clip.DurSec - drag.OrigDur));
                    dur = drag.OrigDur;
                    break;
                }
            }
            Editor.SetEffectRange(drag.ClipId, drag.EffectId, start, dur);
            InvalidateVisual();
        }

        private Clip? FindClipById(string id)
            => Editor?.Document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id);

        private void UpdateKeyDrag(Point pos)
        {
            if (Editor is null || _keyDrag is not { } drag || FindClipById(drag.ClipId) is not { } clip)
                return;
            var target = Snap(Math.Max(0, XToTime(pos.X)), null) - clip.StartSec;
            target = Math.Clamp(target, 0, clip.DurSec);
            if (Math.Abs(target - drag.CurrentSec) < 1e-6)
                return;
            Editor.MoveEffectKeyframe(drag.ClipId, drag.EffectId, drag.CurrentSec, target, drag.Gesture);
            _keyDrag = drag with { CurrentSec = target };
            _editorVm?.Properties.SelectKeyframe(target);
            InvalidateVisual();
        }

        private void EndRackDrag()
        {
            _rackDrag = null;
            _keyDrag = null;
        }

        private static string EasingName(KeyframeEasing easing) => easing switch
        {
            KeyframeEasing.EaseIn => "Ease in",
            KeyframeEasing.EaseOut => "Ease out",
            KeyframeEasing.EaseInOut => "Ease in & out",
            KeyframeEasing.Hold => "Hold (no movement)",
            _ => "Linear",
        };

        /// <summary>Right-click menu for an effect bar, or for one of its keyframes.</summary>
        private bool TryOpenRackMenu(Point pos)
        {
            if (Editor is null || HitTestRack(pos) is not { Item.Effect: { } fx } hit)
                return false;

            _selectedClipId = hit.Clip.Id;
            _selectedTrackId = null;
            _editorVm?.Properties.SelectEffect(hit.Clip.Id, fx.Id);
            InvalidateVisual();

            var menu = new ContextMenu();
            var clip = hit.Clip;

            if (hit.Part == RackPart.Keyframe)
            {
                var time = hit.KeyframeSec;
                _editorVm?.Properties.SelectKeyframe(time);
                var current = fx.Keyframes.Values
                    .SelectMany(t => t).FirstOrDefault(k => Math.Abs(k.TimeSec - time) < 1e-6).Easing;

                var jump = new MenuItem { Header = "Jump to keyframe" };
                jump.Click += (_, _) => SetPlayhead(Editor.SnapTime(clip.StartSec + time));
                menu.Items.Add(jump);

                var duplicate = new MenuItem { Header = "Duplicate to the playhead" };
                duplicate.Click += (_, _) =>
                {
                    Editor.DuplicateEffectKeyframe(clip.Id, fx.Id, time,
                        Math.Max(0, (_editorVm?.PlayheadTimeSec ?? 0) - clip.StartSec));
                    InvalidateVisual();
                };
                menu.Items.Add(duplicate);

                var easing = new MenuItem { Header = "Movement to the next keyframe" };
                foreach (var option in Enum.GetValues<KeyframeEasing>())
                {
                    var item = new MenuItem { Header = (option == current ? "\u2713  " : "     ") + EasingName(option) };
                    item.Click += (_, _) =>
                    {
                        Editor.SetEffectKeyframeEasing(clip.Id, fx.Id, time, option);
                        InvalidateVisual();
                    };
                    easing.Items.Add(item);
                }
                menu.Items.Add(easing);

                menu.Items.Add(new Separator());
                var delete = new MenuItem { Header = "Delete keyframe", Foreground = new SolidColorBrush(Color.Parse("#ff6b6b")) };
                delete.Click += (_, _) =>
                {
                    Editor.RemoveEffectKeyframe(clip.Id, fx.Id, time);
                    if (_editorVm is not null) _editorVm.Properties.SelectedKeyframeSec = null;
                    InvalidateVisual();
                };
                menu.Items.Add(delete);
                menu.Open(this);
                return true;
            }

            var addKey = new MenuItem { Header = "Add keyframe here" };
            var at = Math.Max(0, XToTime(pos.X) - clip.StartSec);
            addKey.Click += (_, _) =>
            {
                if (Editor.AddEffectKeyframe(clip.Id, fx.Id, at) is { } added)
                    _editorVm?.Properties.SelectKeyframe(added);
                InvalidateVisual();
            };
            menu.Items.Add(addKey);

            var animate = new MenuItem { Header = fx.Keyframes.Count > 0 ? "Stop animating (keep the look at the playhead)" : "Animate (start keyframes)" };
            animate.Click += (_, _) =>
            {
                Editor.SetEffectAnimated(clip.Id, fx.Id, fx.Keyframes.Count == 0, Math.Max(0, (_editorVm?.PlayheadTimeSec ?? 0) - clip.StartSec));
                InvalidateVisual();
            };
            menu.Items.Add(animate);

            var savePreset = new MenuItem { Header = "Save as preset..." };
            savePreset.Click += async (_, _) =>
            {
                if (_editorVm is not null)
                    await _editorVm.SaveEffectAsPresetAsync(clip.Id, fx.Id);
            };
            menu.Items.Add(savePreset);
            menu.Items.Add(new Separator());

            var toggle = new MenuItem { Header = fx.Enabled ? "Turn effect off" : "Turn effect on" };
            toggle.Click += (_, _) => { Editor.ToggleEffect(clip.Id, fx.Id); InvalidateVisual(); };
            menu.Items.Add(toggle);

            if (fx.HasRange)
            {
                var whole = new MenuItem { Header = "Apply to the whole clip" };
                whole.Click += (_, _) =>
                {
                    Editor.SetEffectRange(clip.Id, fx.Id, null, null);
                    InvalidateMeasure();
                    InvalidateVisual();
                };
                menu.Items.Add(whole);
            }
            else
            {
                var limit = new MenuItem { Header = "Limit to part of the clip" };
                limit.Click += (_, _) =>
                {
                    var from = Math.Clamp((_editorVm?.PlayheadTimeSec ?? 0) - clip.StartSec, 0, Math.Max(0, clip.DurSec - 0.5));
                    Editor.SetEffectRange(clip.Id, fx.Id, from, Math.Min(3, clip.DurSec - from));
                    InvalidateMeasure();
                    InvalidateVisual();
                };
                menu.Items.Add(limit);
            }

            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Remove effect", Foreground = new SolidColorBrush(Color.Parse("#ff6b6b")) };
            remove.Click += (_, _) => { Editor.RemoveEffect(clip.Id, fx.Id); InvalidateMeasure(); InvalidateVisual(); };
            menu.Items.Add(remove);
            menu.Open(this);
            return true;
        }

        private static string DescribeRack(RackHit hit)
        {
            if (hit.Part == RackPart.Keyframe)
                return "Keyframe \u00b7 drag to retime \u00b7 right-click to change how it moves or delete it";
            var item = hit.Item!;
            string Clock(double t) => $"{(int)(t / 60):00}:{t % 60:00.0}";
            if (item.Effect is null)
                return $"{item.Label} · click to edit";
            var tail = hit.InKeyLane ? "\nDouble-click here to add a keyframe" : "";

            var span = item.Effect.HasRange
                ? $"{Clock(item.StartSec)} → {Clock(item.StartSec + item.DurSec)} of the clip"
                : "the whole clip";
            return $"{item.Label} · {span}\nDrag to move · drag an end to limit it to part of the clip · right-click for more" + tail;
        }
    }
}
