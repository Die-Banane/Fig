using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fig.App.Services;
using Fig.App.ViewModels;
using Fig.Core.Input;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    public partial class TimelineView : Control
    {
        public static DataFormat<MediaAsset> MediaFormat => DragFormats.Media;
        public static DataFormat<EffectCatalogEntry> EffectFormat => DragFormats.Effect;
        public static DataFormat<EffectPreset> PresetFormat => DragFormats.Preset;
        public static DataFormat<TransitionCatalogEntry> TransitionFormat => DragFormats.Transition;

        public static readonly StyledProperty<TimelineEditor?> EditorProperty =
            AvaloniaProperty.Register<TimelineView, TimelineEditor?>(nameof(Editor));

        public static readonly StyledProperty<IReadOnlyDictionary<string, MediaAsset>?> MediaByIdProperty =
            AvaloniaProperty.Register<TimelineView, IReadOnlyDictionary<string, MediaAsset>?>(nameof(MediaById));

        public TimelineEditor? Editor
        {
            get => GetValue(EditorProperty);
            set => SetValue(EditorProperty, value);
        }

        public IReadOnlyDictionary<string, MediaAsset>? MediaById
        {
            get => GetValue(MediaByIdProperty);
            set => SetValue(MediaByIdProperty, value);
        }

        public static readonly StyledProperty<GestureRegistry?> GesturesProperty =
            AvaloniaProperty.Register<TimelineView, GestureRegistry?>(nameof(Gestures));

        public GestureRegistry? Gestures
        {
            get => GetValue(GesturesProperty);
            set => SetValue(GesturesProperty, value);
        }

        public TimelineViewport Viewport { get; } = new();

        private double _dropTimeSec = -1;

        // ripple-slide animation: draw at StartSec + offset, ease offset → 0
        private readonly Dictionary<string, (double FromOffsetSec, long StartTimestamp)> _rippleSlides = new();

        private EditorViewModel? _editorVm;

        private string? _selectedClipId
        {
            get => Editor?.Selection.SelectedClipIds.FirstOrDefault();
            set
            {
                if (Editor is null) return;
                if (value is null) Editor.Selection.Clear();
                else
                {
                    var ids = LinkedSelectionOn
                        ? Editor.LinkGroup(value).Select(c => c.Id).ToList()
                        : new List<string> { value };
                    Editor.Selection.SelectClips(ids.Count == 0 ? new List<string> { value } : ids);
                }
            }
        }

        private string? _selectedTrackId
        {
            get => Editor?.Selection.ActiveTrackId;
            set
            {
                if (Editor is not null) Editor.Selection.ActiveTrackId = value;
            }
        }

        private string? _selectedMarkerId => Editor?.Selection.SelectedMarkerId;

        // transition drag state
        private bool _draggingTransition;
        private string? _dragTransitionKey;
        private double _dragTransitionCutSec;
        private double _dragTransitionMaxSec;

        // playhead
        public double PlayheadTimeSec { get; private set; }
        public event Action<double>? PlayheadChanged;
        private bool _draggingPlayhead;

        /// <summary>User-scrub: moves the playhead and notifies listeners (seek happens in the view model).</summary>
        private void SetPlayhead(double sec)
        {
            PlayheadTimeSec = sec;
            PlayheadChanged?.Invoke(sec);
            InvalidateVisual();
        }

        /// <summary>Playback-driven: moves the playhead visually without re-triggering a seek.</summary>
        public void SetPlayheadFromPlayback(double sec)
        {
            PlayheadTimeSec = sec;
            InvalidateVisual();
        }

        public void ZoomInAtPlayhead()
        {
            ZoomAtTimelineX(PlayheadTimeSecToX(), ZoomFactor);
        }

        public void ZoomOutAtPlayhead()
        {
            ZoomAtTimelineX(PlayheadTimeSecToX(), 1.0 / ZoomFactor);
        }

        public void ZoomToFitSequence(double endTimeSec)
        {
            var width = Math.Max(1, Bounds.Width - TrackHeaderWidth);
            var duration = Math.Max(0.5, endTimeSec);
            Viewport.SetPixelsPerSecond(Math.Clamp(width / duration, TimelineViewport.MinPixelsPerSecond, TimelineViewport.MaxPixelsPerSecond));
            Viewport.SetScrollTime(0);
            InvalidateVisual();
        }

        private double PlayheadTimeSecToX()
        {
            var width = Math.Max(1, Bounds.Width - TrackHeaderWidth);
            var x = Viewport.TimeToX(PlayheadTimeSec);
            return Math.Clamp(x, 0, width);
        }

        private void ZoomAtTimelineX(double cursorX, double factor)
        {
            Viewport.ZoomAt(cursorX, factor);
            InvalidateVisual();
        }

        // pan state
        private bool _panning;
        private double _panStartX;
        private double _panStartScroll;

        // clip drag state
        private bool _draggingClip;
        private Clip? _dragClip;
        private Track? _dragOriginalTrack;
        private double _dragStartSec;
        private double _dragSrcInSec;
        private double _dragSrcOutSec;
        private double _dragPointerTime;
        private double _dragOriginalDurSec;
        private readonly List<Clip> _dragMembers = new();
        private IReadOnlyList<ClipGeometry> _dragBefore = Array.Empty<ClipGeometry>();
        private KeyModifiers _modifiersAtPress;
        private readonly Dictionary<string, double> _dragOriginals = new();
        private readonly Dictionary<string, double> _dragOriginalDurs = new();
        private readonly Dictionary<string, double> _dragSrcIns = new();
        private readonly Dictionary<string, double> _dragSrcOuts = new();

        private static readonly IBrush SelectionBrush = EditorTheme.AccentBrush;

        private static readonly IBrush SurfaceBackground = EditorTheme.CardBrush;
        private static readonly IBrush TrackLaneBrush = EditorTheme.CardBrush;
        private static readonly IBrush TrackLaneAltBrush = new SolidColorBrush(EditorTheme.TrackLaneAlt);
        private static readonly IBrush RulerBackground = new SolidColorBrush(EditorTheme.RulerBackground);
        private static readonly IBrush BorderBrush = EditorTheme.BorderBrush;
        private static readonly IBrush RulerTextBrush = EditorTheme.TextMutedBrush;
        private static readonly IBrush SelectedLaneTintBrush = new SolidColorBrush(EditorTheme.SelectedLaneTint);
        private static readonly IBrush DeleteHoverBrush = new SolidColorBrush(Color.Parse("#5a1f1f"));

        // Clip fills
        private static readonly IBrush VideoBrush = new SolidColorBrush(Color.Parse("#1679db"));
        private static readonly IBrush AudioBrush = new SolidColorBrush(Color.Parse("#2d674a"));
        private static readonly IBrush AudioWaveformBrush = new SolidColorBrush(Color.Parse("#8bc9a3"));
        private static readonly IBrush TextClipBrush = new SolidColorBrush(Color.Parse("#a84f7d"));
        private static readonly IBrush ImageClipBrush = new SolidColorBrush(Color.Parse("#5e57a8"));

        private static readonly IBrush VideoBorder = new SolidColorBrush(Color.Parse("#55a2b2"));
        private static readonly IBrush AudioBorder = new SolidColorBrush(Color.Parse("#589b74"));
        private static readonly IBrush TextBorder = new SolidColorBrush(Color.Parse("#d17aa6"));
        private static readonly IBrush ImageBorder = new SolidColorBrush(Color.Parse("#8b84d6"));

        private static readonly IBrush ClipShadow = new SolidColorBrush(Color.Parse("#00000060"));
        private static readonly IBrush TrackHeaderTextBrush = EditorTheme.TextMutedBrush;
        private static readonly IBrush HeaderIconDimBrush = new SolidColorBrush(Color.Parse("#6a6a6a"));
        private static readonly IBrush TrackHeaderBrush = new SolidColorBrush(Color.Parse("#191b1e"));
        private static readonly IBrush ClipSelectionBrush = new SolidColorBrush(Color.Parse("#d9e0e7"));

        // transitions
        private static readonly IBrush TransitionBandBrush = new SolidColorBrush(Color.FromArgb(70, 0xe0, 0xa3, 0x08));
        private static readonly IBrush TransitionBandSelectedBrush = new SolidColorBrush(Color.FromArgb(120, 0xea, 0xb3, 0x08));
        private static readonly IBrush TransitionBadgeBrush = new SolidColorBrush(Color.FromArgb(235, 0xe0, 0xa3, 0x08));

        /// <summary>Top strip of the ruler area: range bars and bookmark flags.</summary>
        private const double MarkerLaneHeight = 20;
        private const double PlayheadCapHeight = 8;
        private const double TickRulerHeight = 22;
        private const double RulerHeight = MarkerLaneHeight + TickRulerHeight;
        private const double ClipCornerRadius = 6;
        private const double TrackHeaderWidth = 118;
        private const double ZoomFactor = 1.25;
        private const double HeaderButtonSize = 22;
        private const double HeaderButtonGap = 4;
        private const double HeaderPaddingLeft = 7;
        private const double TrackAccentBarWidth = 2;
        private const double EmptyTrackHeight = 34;

        public TimelineView()
        {
            ClipToBounds = true;
            Focusable = true;
            RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            DragDrop.SetAllowDrop(this, true);
            DragDrop.AddDragOverHandler(this, OnDragOver);
            DragDrop.AddDropHandler(this, OnDrop);
            DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == EditorProperty)
            {
                if (change.OldValue is TimelineEditor oldEditor)
                {
                    oldEditor.TimelineChanged -= OnTimelineChanged;
                    oldEditor.Selection.Changed -= OnTimelineSelectionChanged;
                    oldEditor.BeatPreviewChanged -= OnBeatPreviewChanged;
                }
                if (change.NewValue is TimelineEditor newEditor)
                {
                    newEditor.TimelineChanged += OnTimelineChanged;
                    newEditor.Selection.Changed += OnTimelineSelectionChanged;
                    newEditor.BeatPreviewChanged += OnBeatPreviewChanged;
                }
                // project/editor swap — drop any in-flight slide offsets
                ClearRippleSlides();
                InvalidateMeasure();
                InvalidateVisual();
            }
            else if (change.Property == MediaByIdProperty)
            {
                InvalidateVisual();
            }
            else if (change.Property == DataContextProperty)
            {
                BindEditorViewModel(DataContext as EditorViewModel);
            }
        }

        /// <summary>
        /// Height needed to show the ruler, every track lane, and the add-track control.
        /// Used so a parent <see cref="ScrollViewer"/> can scroll when tracks overflow.
        /// </summary>
        private double ComputeContentHeight()
        {
            var lanes = Editor is null || Editor.Document.Tracks.Count == 0
                ? TimelineGeometry.TrackHeight
                : Editor.Document.Tracks.Sum(TrackHeight);
            return RulerHeight + lanes + HeaderButtonSize + 12;
        }

        private double TrackHeight(Track track) =>
            track.Clips.Count == 0 && track.Markers.Count == 0 && track.Effects.Count == 0
                ? EmptyTrackHeight
                : TimelineGeometry.LaneHeight(track) + TrackRackHeight(track);

        private double TrackTop(int index)
        {
            if (Editor is null || index <= 0)
                return RulerHeight;

            var top = RulerHeight;
            for (var i = 0; i < Math.Min(index, Editor.Document.Tracks.Count); i++)
                top += TrackHeight(Editor.Document.Tracks[i]);
            return top;
        }

        private double TrackBottom(int index) =>
            TrackTop(index) + TrackHeight(Editor!.Document.Tracks[index]);

        protected override Size MeasureOverride(Size availableSize)
        {
            var height = ComputeContentHeight();
            // Outside a scroll viewer, fill the given slot; inside one, grow with track count.
            if (!double.IsInfinity(availableSize.Height))
                height = Math.Max(height, availableSize.Height);

            var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            return new Size(width, height);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            BindEditorViewModel(DataContext as EditorViewModel);
            _scroller = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroller is not null)
                _scroller.ScrollChanged += OnScrollerChanged;
        }

        // The ruler and marker lane are pinned to the top of the pane: the track area scrolls under them.
        private ScrollViewer? _scroller;

        private void OnScrollerChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

        /// <summary>How far the pane is scrolled; the ruler is drawn this far down so it stays at the top of the view.</summary>
        private double StickyOffset
            => _scroller is null ? 0 : Math.Clamp(_scroller.Offset.Y, 0, Math.Max(0, Bounds.Height - RulerHeight));

        /// <summary>
        /// A pointer position with the pinned ruler taken into account: over the ruler it is reported in the
        /// ruler's own coordinates (0 to <see cref="RulerHeight"/>), anywhere else it is unchanged.
        /// </summary>
        private Point RulerSpace(Point p)
        {
            var offset = StickyOffset;
            return offset > 0 && p.Y >= offset && p.Y < offset + RulerHeight ? new Point(p.X, p.Y - offset) : p;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            BindEditorViewModel(null);
            ClearRippleSlides();
            if (_scroller is not null)
                _scroller.ScrollChanged -= OnScrollerChanged;
            _scroller = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void BindEditorViewModel(EditorViewModel? vm)
        {
            if (ReferenceEquals(_editorVm, vm))
                return;
            if (_editorVm is not null)
                _editorVm.RippleSlideStarted -= OnRippleSlideStarted;
            _editorVm = vm;
            if (_editorVm is not null)
                _editorVm.RippleSlideStarted += OnRippleSlideStarted;
        }

        private void OnTimelineSelectionChanged() => InvalidateVisual();

        private void OnBeatPreviewChanged() => InvalidateVisual();

        private void OnTimelineChanged()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            // Plain wheel always scrolls tracks via the parent ScrollViewer.
            // Ctrl+wheel zooms; Shift+wheel pans time horizontally.
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)
                && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                return;

            var pattern = new GesturePattern
            {
                Wheel = true,
                WheelDir = e.Delta.Y > 0 ? WheelDirection.Up : WheelDirection.Down,
                Ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control),
                Shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            };

            var gesture = Gestures?.Resolve(pattern) ?? TimelineGesture.None;
            var pos = e.GetPosition(this);

            switch (gesture)
            {
                case TimelineGesture.ZoomIn:
                    Viewport.ZoomAt(pos.X - TrackHeaderWidth, 1.25);
                    break;
                case TimelineGesture.ZoomOut:
                    Viewport.ZoomAt(pos.X - TrackHeaderWidth, 1 / 1.25);
                    break;
                case TimelineGesture.ScrollHorizontal:
                    Viewport.ScrollBy(e.Delta.X != 0 ? e.Delta.X : -e.Delta.Y);
                    break;
                default:
                    return;
            }

            InvalidateVisual();
            e.Handled = true;
        }

        // ---- drag & drop ----

        private double XToTime(double x) => Viewport.XToTime(x - TrackHeaderWidth);

        private double XToTime(DragEventArgs e) => XToTime(e.GetPosition(this).X);

        private string? ClipName(Clip clip)
        {
            if (clip is TextClip text)
                return string.IsNullOrWhiteSpace(text.Text) ? "Text" : text.Text.Replace('\n', ' ');
            if (clip.MediaId is { } mediaId && MediaById is not null && MediaById.TryGetValue(mediaId, out var asset))
                return asset.FileName;
            return clip.DisplayName;
        }

        private static readonly Pen FilmstripSeamPen = new(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1);

        private bool IsMarkerHot(Marker m) => Editor?.Selection.IsMarkerSelected(m.Id) == true || _hoverMarkerId == m.Id;

        /// <summary>Returns the clip keyframe diamond under the pointer, or null.</summary>
        private (Clip Clip, double TimeSec)? HitTestKeyframe(Point pos)
        {
            if (Editor is null || pos.X <= TrackHeaderWidth)
                return null;
            const double grabPx = 5;
            for (var i = 0; i < Editor.Document.Tracks.Count; i++)
            {
                var track = Editor.Document.Tracks[i];
                if (track.Kind != TrackKind.Video)
                    continue;
                var top = TrackTop(i);
                if (pos.Y < top || pos.Y >= TrackBottom(i))
                    continue;
                // clip automation diamonds render along the top edge of the body (effect keyframes live on the effect bars)
                var bodyTop = top + TimelineGeometry.ClipInset + TimelineGeometry.LabelHeight(track);
                var inTopBand = pos.Y >= bodyTop - 2 && pos.Y <= bodyTop + 14;
                foreach (var clip in track.Clips)
                {
                    if (inTopBand)
                        foreach (var (_, trackKf) in clip.Keyframes)
                            foreach (var kf in trackKf)
                            {
                                var x = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec + kf.TimeSec);
                                if (Math.Abs(pos.X - x) <= grabPx)
                                    return (clip, clip.StartSec + kf.TimeSec);
                            }
                }
            }
            return null;
        }

        /// <summary>Returns the transition whose cut badge is near the pointer, or null.</summary>
        private CutTransition? HitTestTransition(Point pos)
        {            if (Editor is null)
                return null;

            for (var i = 0; i < Editor.Document.Tracks.Count; i++)
            {
                var track = Editor.Document.Tracks[i];
                var top = TrackTop(i);
                if (pos.Y < top || pos.Y >= TrackBottom(i))
                    continue;
                // the badge sits at the clip body's vertical center; only grab within that band so
                // markers / keyframes near the cut stay clickable
                var centerY = top + TimelineGeometry.ClipInset + TimelineGeometry.LabelHeight(track) + TimelineGeometry.BodyHeight(track) / 2;
                if (pos.Y < centerY - 16 || pos.Y > centerY + 16)
                    continue;
                foreach (var t in Editor.EnumerateTransitions(track))
                {
                    var cutX = TrackHeaderWidth + Viewport.TimeToX(t.CutSec);
                    if (Math.Abs(pos.X - cutX) <= 15)
                        return t;
                }
            }
            return null;
        }

        private enum DragMode { None, Move, ResizeStart, ResizeEnd, FadeIn, FadeOut }

        private DragMode _dragMode = DragMode.None;
        private enum EdgeKind { None, Left, Right, FadeIn, FadeOut }
        private EdgeKind _hoverEdge;

        private const double TrimEdgePx = 6;
        private const double FadeZonePx = 14;

        private static readonly IBrush AudioBodyBrush = new SolidColorBrush(Color.Parse("#15241d"));
        private static readonly IBrush AudioWaveFill = new SolidColorBrush(Color.Parse("#74d6a0"));
        private static readonly Pen AudioCenterPen = new(new SolidColorBrush(Color.FromArgb(70, 0x74, 0xd6, 0xa0)), 1);

        // header button columns: [0] = mute/visibility, [1] = delete
        private const int HeaderToggleColumn = 0;
        private const int HeaderDeleteColumn = 1;
        private const int HeaderRackColumn = 2;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            NoteModifiers(e.KeyModifiers);
            _modifiersAtPress = e.KeyModifiers;
            if (Editor is null)
                return;

            Focus();
            var pos = RulerSpace(e.GetPosition(this));

            // right-click: context menu (add marker / enable-disable / delete marker)
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                OpenContextMenu(pos);
                e.Handled = true;
                return;
            }

            if (TryArmCopyDrag(e, pos))
            {
                e.Handled = true;
                return;
            }

            // effect rack under a clip: expand/collapse, select, drag to move or resize
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && HitTestRack(pos) is { } rackHit && HandleRackPress(rackHit, pos, e.ClickCount))
            {
                e.Handled = true;
                return;
            }

            // middle-button pan (time axis). Always available — not gated on Ctrl.
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                var panGesture = Gestures?.Resolve(new GesturePattern { Button = Fig.Core.Input.MouseButton.Middle })
                    ?? TimelineGesture.Pan;
                if (panGesture == TimelineGesture.Pan || Gestures is null)
                {
                    _panning = true;
                    _panStartX = pos.X;
                    _panStartScroll = Viewport.ScrollTime;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            // transition badge hit-test: select + drag to resize; double-click seeks to the cut
            var transitionHit = HitTestTransition(pos);
            if (transitionHit is not null)
            {
                if (e.ClickCount >= 2)
                    SetPlayhead(Editor.SnapTime(Math.Max(0, transitionHit.CutSec)));
                Editor.Selection.SelectTransition(transitionHit.Key);
                _dragTransitionKey = transitionHit.Key;
                _dragTransitionCutSec = transitionHit.CutSec;
                _dragTransitionMaxSec = Math.Max(0.05,
                    Editor.GetMaxTransitionDuration(transitionHit.LeftClipId, transitionHit.RightClipId));
                _draggingTransition = true;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // keyframe diamond hit-test: seek the playhead to it
            if (HitTestKeyframe(pos) is { } keyframe)
            {
                _selectedClipId = keyframe.Clip.Id;
                _selectedTrackId = null;
                Editor.Selection.SelectOnly(keyframe.Clip.Id);
                SetPlayhead(Editor.SnapTime(Math.Max(0, keyframe.TimeSec)));
                e.Handled = true;
                return;
            }

            // marker hit-test: takes priority over playhead drag (ruler) and clip drag.
            // double-click on a marker seeks the playhead to it.
            var markerHit = HitTestMarker(pos);
            if (markerHit is not null)
            {
                if (e.ClickCount >= 2)
                    SetPlayhead(Editor.SnapTime(Math.Max(0, AbsoluteMarkerTime(markerHit))));
                bool toggleMarker = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
                bool extendMarkers = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                Editor.SelectMarker(markerHit.Marker.Id, toggleMarker, extendMarkers);
                if (toggleMarker || extendMarkers)
                {
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
                _dragMarkerId = markerHit.Marker.Id;
                _dragMarkerStartSec = AbsoluteMarkerTime(markerHit);
                _dragRangeGrab = HitTestRangeGrab(markerHit.Marker, pos);
                _dragRangeOffsetSec = XToTime(pos.X) - markerHit.Marker.StartSec;
                _draggingMarker = true;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // playhead drag: click in ruler area
            if (pos.Y <= RulerHeight)
            {
                _draggingPlayhead = true;
                SetPlayhead(Snap(XToTime(pos.X), includePlayhead: false));
                e.Handled = true;
                return;
            }

            if (pos.X <= TrackHeaderWidth)
            {
                var track = HitTestTrack(pos.Y);
                if (track is not null)
                {
                    var toggleRect = HeaderButtonRect(track, HeaderToggleColumn);
                    var delRect = HeaderButtonRect(track, HeaderDeleteColumn);
                    if (TrackHasRack(track) && RackToggleRect(track).Contains(pos))
                    {
                        SetTrackRackExpanded(track.Id, !IsTrackRackExpanded(track));
                        e.Handled = true;
                        return;
                    }
                    if (toggleRect.Contains(pos))
                    {
                        // video -> visibility toggle; audio -> mute toggle
                        if (track.Kind == TrackKind.Video)
                            Editor.SetTrackVisible(track.Id, !track.Visible);
                        else
                            Editor.SetTrackMuted(track.Id, !track.Muted);
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }
                    if (delRect.Contains(pos))
                    {
                        Editor.RemoveTrack(track.Id);
                        _selectedTrackId = null;
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }
                    _selectedClipId = null;
                    _selectedTrackId = track.Id;
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            // add-track button at bottom of header column: ask which kind first
            var addRect = AddTrackButtonRect();
            if (addRect.Contains(pos))
            {
                var menu = new ContextMenu
                {
                    Items =
                    {
                        new MenuItem { Header = "Video clip" },
                        new MenuItem { Header = "Audio clip" },
                    },
                };
                if (menu.Items[0] is MenuItem addVideo)
                    addVideo.Click += (_, _) =>
                    {
                        Editor.AddTrack(TrackKind.Video);
                        InvalidateVisual();
                    };
                if (menu.Items[1] is MenuItem addAudio)
                    addAudio.Click += (_, _) =>
                    {
                        Editor.AddTrack(TrackKind.Audio);
                        InvalidateVisual();
                    };
                menu.Open(this);
                e.Handled = true;
                return;
            }

            // clip hit-testing: press on a clip starts move/resize drag
            var time = Viewport.XToTime(pos.X - TrackHeaderWidth);
            var clipTrack = HitTestTrack(pos.Y);
            if (clipTrack is not null)
            {
                var clip = ClipAt(pos, out _);
                if (clip is not null)
                {
                    // Shift or Ctrl held: draw a selection region from here (a plain click still adds the clip)
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        ArmMarquee(pos, e.KeyModifiers);
                        _marqueeClickClip = clip;
                        e.Pointer.Capture(this);
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                    }
                    ClickSelectClip(clip, e.KeyModifiers);
                    _pressedPx = pos.X;
                    BeginClipDrag(clip, clipTrack, time, pos);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            // empty space: a click deselects, a drag draws a selection box
            ArmMarquee(pos, e.KeyModifiers);
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            NoteModifiers(e.KeyModifiers);

            if (Editor is null)
                return;

            // while something is being dragged the pointer keeps its content coordinates; hovering and
            // scrubbing the playhead use the pinned ruler's
            var pos = e.GetPosition(this);
            if (_draggingPlayhead || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                pos = RulerSpace(pos);

            if (UpdateCopyDrag(pos)) return;

            if (_panning)
            {
                var dx = pos.X - _panStartX;
                Viewport.SetScrollTime(_panStartScroll - dx / Viewport.PixelsPerSecond);
                InvalidateVisual();
                return;
            }

            if (_draggingTransition && _dragTransitionKey is not null)
            {
                var cutX = TrackHeaderWidth + Viewport.TimeToX(_dragTransitionCutSec);
                var dx = Math.Abs(pos.X - cutX) / Math.Max(1, Viewport.PixelsPerSecond);
                var dur = Math.Clamp(2 * dx, 0.05, _dragTransitionMaxSec);
                Editor.SetTransitionDuration(_dragTransitionKey, Editor.SnapTime(dur));
                InvalidateVisual();
                return;
            }

            if (_keyDrag is not null)
            {
                UpdateKeyDrag(pos);
                return;
            }

            if (_rackDrag is not null)
            {
                UpdateRackDrag(pos);
                return;
            }

            if (UpdateMarquee(pos))
                return;

            if (_draggingMarker && _dragMarkerId is not null)
            {
                var loc = Editor.FindMarker(_dragMarkerId);
                if (loc is null)
                {
                    _draggingMarker = false;
                    return;
                }
                if (loc.Marker.IsRange && _dragRangeGrab != RangeGrab.None)
                {
                    var t = Snap(Math.Max(0, XToTime(pos.X)), null, loc.Marker.Id);
                    var m = loc.Marker;
                    const double minLength = 0.1;
                    switch (_dragRangeGrab)
                    {
                        case RangeGrab.Start:
                        {
                            var start = Math.Min(t, m.EndSec - minLength);
                            Editor.EditMarker(m.Id, new MarkerEdit(StartSec: start, DurSec: m.EndSec - start));
                            break;
                        }
                        case RangeGrab.End:
                            Editor.EditMarker(m.Id, new MarkerEdit(DurSec: Math.Max(minLength, t - m.StartSec)));
                            break;
                        default:
                            Editor.EditMarker(m.Id, new MarkerEdit(
                                StartSec: Snap(Math.Max(0, XToTime(pos.X) - _dragRangeOffsetSec), null, m.Id)));
                            break;
                    }
                    InvalidateVisual();
                    return;
                }
                var raw = loc.Clip is not null ? XToTime(pos.X) - loc.Clip.StartSec : XToTime(pos.X);
                Editor.MoveMarker(_dragMarkerId, Snap(Math.Max(0, raw), null, _dragMarkerId));
                InvalidateVisual();
                return;
            }

            if (_draggingPlayhead)
            {
                SetPlayhead(Math.Max(0, Snap(XToTime(pos.X), includePlayhead: false)));
                return;
            }

            if (_draggingClip && _dragClip is not null)
            {
                var pointerTime = Math.Max(0, Viewport.XToTime(pos.X - TrackHeaderWidth));
                var deltaTime = pointerTime - _dragPointerTime;

                switch (_dragMode)
                {
                    case DragMode.Move:
                    {
                        // snap the grabbed clip, then move everything acting with it by the same amount
                        var origin = _dragOriginals[_dragClip.Id];
                        var members = _dragMembers.Select(m => m.Id).ToHashSet();
                        var rawStart = Math.Max(0, origin + deltaTime);
                        var snappedStart = Snap(rawStart, _dragClip.Id, excludeClips: members);
                        var delta = snappedStart - origin;
                        var earliest = _dragOriginals.Values.Min();
                        delta = Math.Max(delta, -earliest);                      // nothing may go before the start

                        // dragging a single clip (with its partners) onto another track moves it there
                        var hoverTrack = HitTestTrack(pos.Y);
                        if (_dragMembers.Count == Editor.LinkGroup(_dragClip.Id).Count && LinkedSelectionOn
                            && hoverTrack is not null && hoverTrack.Id != _dragOriginalTrack?.Id
                            && Editor.MoveClipToTrack(_dragClip.Id, hoverTrack.Id))
                        {
                            _dragOriginalTrack = hoverTrack;
                            break;
                        }

                        var targets = _dragMembers.ToDictionary(m => m.Id, m => _dragOriginals[m.Id] + delta);
                        if (!Editor.WouldOverlapMove(targets))
                            foreach (var m in _dragMembers)
                                m.StartSec = targets[m.Id];
                        // selected effect bars come along by however far the clips really moved
                        Carry(_dragClip.StartSec - origin);
                        break;
                    }
                    case DragMode.ResizeStart:
                    {
                        UndoNudge();
                        ApplyLiveResizeStart(deltaTime);
                        // the clips pushed left ran out of room: give back what they could not take
                        if (Nudge(pushRight: false) is var deficit and > 0)
                        {
                            ApplyLiveResizeStart(deltaTime + deficit);
                            Nudge(pushRight: false);
                        }
                        break;
                    }
                    case DragMode.ResizeEnd:
                        UndoNudge();
                        ApplyLiveResizeEnd(deltaTime);
                        Nudge(pushRight: true);
                        break;
                    case DragMode.FadeIn:
                    {
                        var fade = Math.Max(0, pointerTime - _dragClip.StartSec);
                        Editor.SetFadeIn(_dragClip.Id, fade);
                        break;
                    }
                    case DragMode.FadeOut:
                    {
                        var end = _dragClip.StartSec + _dragClip.DurSec;
                        var fade = Math.Max(0, end - pointerTime);
                        Editor.SetFadeOut(_dragClip.Id, fade);
                        break;
                    }
                }
                InvalidateVisual();
                return;
            }

            // idle: effect bars show what they are and how to use them
            if (HitTestRack(pos) is { } rackOver)
            {
                Cursor = rackOver.Part is RackPart.Left or RackPart.Right
                    ? new Cursor(StandardCursorType.SizeWestEast)
                    : new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(this, DescribeRack(rackOver));
                return;
            }

            // idle: markers highlight, show their name and time, and use a hand (or resize) cursor
            var hoverMarker = HitTestMarker(pos);
            var hoverMarkerId = hoverMarker?.Marker.Id;
            if (hoverMarkerId != _hoverMarkerId)
            {
                _hoverMarkerId = hoverMarkerId;
                ToolTip.SetTip(this, hoverMarker is null ? null : DescribeMarker(hoverMarker));
                InvalidateVisual();
            }
            if (hoverMarker is not null)
            {
                Cursor = hoverMarker.Marker.IsRange && HitTestRangeGrab(hoverMarker.Marker, pos) is RangeGrab.Start or RangeGrab.End
                    ? new Cursor(StandardCursorType.SizeWestEast)
                    : new Cursor(StandardCursorType.Hand);
                return;
            }

            // idle: reflect resize / fade affordance when hovering a clip edge
            var overEdge = HoverClipEdge(pos);
            Cursor = overEdge switch
            {
                EdgeKind.Left or EdgeKind.Right or EdgeKind.FadeIn or EdgeKind.FadeOut
                    => new Cursor(StandardCursorType.SizeWestEast),
                _ => Cursor.Default,
            };
            var hoverChanged = _hoverEdge != overEdge;
            _hoverEdge = overEdge;

            // header button hover (for delete/toggle affordance)
            var newHeaderHover = HoverHeaderButton(pos);
            if (newHeaderHover != _hoverHeaderButton)
            {
                _hoverHeaderButton = newHeaderHover;
                hoverChanged = true;
            }

            if (hoverChanged)
                InvalidateVisual();
        }

        private void ApplyLiveResizeStart(double deltaTime)
        {
            if (_dragClip is null)
                return;

            // the moving edge is the clip start; snap it to other clips' boundaries
            var rawStart = Math.Max(0, _dragOriginals[_dragClip.Id] + deltaTime);
            var snappedStart = Snap(rawStart, _dragClip.Id);
            var snapDelta = snappedStart - _dragOriginals[_dragClip.Id];

            foreach (var c in _dragMembers)
            {
                var orig = _dragOriginals[c.Id];
                var origEnd = orig + _dragOriginalDurs[c.Id];
                var memberStart = Math.Max(0, orig + snapDelta);
                var memberDur = Math.Max(0.1, origEnd - memberStart);

                c.StartSec = memberStart;
                c.DurSec = memberDur;

                if (c.HasSourceRange)
                {
                    var speed = c.Speed;
                    var srcOut = _dragSrcOuts[c.Id];
                    var newSrcIn = _dragSrcIns[c.Id] + (memberStart - orig) * speed;

                    // clamp: can't pull the in-point before the start of the source
                    newSrcIn = Math.Max(0, newSrcIn);
                    if (newSrcIn >= srcOut)
                        newSrcIn = Math.Max(0, srcOut - 0.1);

                    ClipFactory.SetSourceRange(c, newSrcIn, srcOut);

                    // timeline duration derives from the clamped source range
                    c.DurSec = Math.Max(0.1, (srcOut - newSrcIn) / speed);
                }
            }
        }

        private void ApplyLiveResizeEnd(double deltaTime)
        {
            if (_dragClip is null)
                return;

            // the moving edge is the clip end; snap it to other clips' boundaries
            var rawEnd = Math.Max(_dragOriginals[_dragClip.Id] + 0.1, _dragOriginals[_dragClip.Id] + _dragOriginalDurs[_dragClip.Id] + deltaTime);
            var snappedEnd = Snap(rawEnd, _dragClip.Id);
            var snapDelta = snappedEnd - (_dragOriginals[_dragClip.Id] + _dragOriginalDurs[_dragClip.Id]);

            foreach (var c in _dragMembers)
            {
                var orig = _dragOriginals[c.Id];
                var origEnd = orig + _dragOriginalDurs[c.Id];

                // clamp against the source media's actual length
                var maxSrcOut = SourceDurationSec(c);
                var srcIn = _dragSrcIns[c.Id];

                var speed = c.Speed;
                // a clip without a source range (text, a still) can be made any length
                var maxDur = !c.HasSourceRange ? double.MaxValue
                    : maxSrcOut is double max && max > srcIn
                        ? (max - srcIn) / speed
                        : _dragOriginalDurs[c.Id];

                var newEnd = Math.Max(orig + 0.1, origEnd + snapDelta);
                var newDur = Math.Max(0.1, newEnd - orig);
                if (maxDur > 0.1)
                    newDur = Math.Min(newDur, maxDur);

                c.DurSec = newDur;

                if (c.HasSourceRange)
                {
                    var newSrcOut = srcIn + newDur * speed;
                    newSrcOut = maxSrcOut is double m ? Math.Min(newSrcOut, m) : newSrcOut;
                    ClipFactory.SetSourceRange(c, srcIn, newSrcOut);
                }
            }
        }

        /// <summary>Returns the source media's duration in seconds for a clip, or null if unknown.</summary>
        private double? SourceDurationSec(Clip clip)
        {
            if (MediaById is null)
                return null;
            if (!clip.HasSourceRange || clip.MediaId is not { } sourceId || !MediaById.TryGetValue(sourceId, out var asset))
                return null;
            return asset.DurationSec > 0 ? asset.DurationSec : null;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoverEdge = EdgeKind.None;
            _hoverHeaderButton = null;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (FinishCopyDrag(e))
            {
                e.Handled = true;
                return;
            }
            ClearSnapGuide();

            FinishMarquee(e.KeyModifiers);
            if (e.Pointer.Captured == this)
                e.Pointer.Capture(null);

            if (_draggingClip && _dragClip is not null)
            {
                _draggingClip = false;
                // the drag changed clips live; record it now as a single undo step
                Editor?.CommitClipGeometry(_dragBefore);
                EndCarry();
                // clicking (not dragging) a clip inside a multi-selection narrows the selection to it
                if (_collapseToClipId is { } only && Math.Abs(_pressedPx - e.GetPosition(this).X) < MarqueeStartPx
                    && Editor is not null && Editor.FindClipForView(only) is { } c)
                    Editor.Selection.SelectClips(WithLinked(c, e.KeyModifiers).Select(x => x.Id));
                _collapseToClipId = null;
                InvalidateVisual();
            }
            EndRackDrag();
            _draggingMarker = false;
            _dragMarkerId = null;
            _dragRangeGrab = RangeGrab.None;
            _draggingTransition = false;
            _dragTransitionKey = null;
            _draggingPlayhead = false;
            if (_panning)
            {
                _panning = false;
                if (e.Pointer.Captured == this)
                    e.Pointer.Capture(null);
            }
        }

        private void BeginClipDrag(Clip clip, Track track, double time, Point pos)
        {
            var pointerX = pos.X;

            _dragClip = clip;
            _dragOriginalTrack = track;
            _dragStartSec = clip.StartSec;
            _dragOriginalDurSec = clip.DurSec;   // <-- capture fixed baseline
            _dragSrcInSec = clip.SourceIn;
            _dragSrcOutSec = clip.SourceOut;
            _dragPointerTime = Viewport.XToTime(pointerX - TrackHeaderWidth);

            var edge = EdgeAt(clip, track, pos);
            _dragMode = edge switch
            {
                EdgeKind.Left => DragMode.ResizeStart,
                EdgeKind.Right => DragMode.ResizeEnd,
                EdgeKind.FadeIn => DragMode.FadeIn,
                EdgeKind.FadeOut => DragMode.FadeOut,
                _ => DragMode.Move,
            };

            // who acts: a move carries everything selected; a trim or fade acts on the clip and its partners
            _dragMembers.Clear();
            if (_dragMode == DragMode.Move && Editor!.Selection.IsSelected(clip.Id))
            {
                foreach (var id in Editor.Selection.SelectedClipIds)
                    if (Editor.FindClipForView(id) is { } selected)
                        _dragMembers.Add(selected);
            }
            if (_dragMembers.Count == 0)
                _dragMembers.AddRange(WithLinked(clip, _modifiersAtPress));

            _dragOriginals.Clear();
            _dragOriginalDurs.Clear();
            _dragSrcIns.Clear();
            _dragSrcOuts.Clear();
            foreach (var c in _dragMembers)
            {
                _dragOriginals[c.Id] = c.StartSec;
                _dragOriginalDurs[c.Id] = c.DurSec;
                // every clip has a source range; one without a file (text, a still) plays itself, 0 → its length
                _dragSrcIns[c.Id] = c.SourceIn;
                _dragSrcOuts[c.Id] = c.SourceOut;
            }
            _dragBefore = _dragMembers.Select(ClipGeometry.Capture).ToList();
            _draggingClip = true;
            if (_dragMode == DragMode.Move)
                BeginCarryForClips(_dragMembers);
            if (_dragMode is DragMode.ResizeStart or DragMode.ResizeEnd)
                BeginNudge();
        }

        /// <summary>
        /// What a press at <paramref name="pos"/> on <paramref name="clip"/> grabs. The outer TrimEdgePx of each end
        /// trims, at any height. Fades live in the top half only (the next FadeZonePx inward, or an existing fade's
        /// tip), so the lower half of an edge always trims and a trim never catches a fade by accident.
        /// </summary>
        private EdgeKind EdgeAt(Clip clip, Track track, Point pos)
        {
            var px = Viewport.PixelsPerSecond;
            var leftX = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec);
            var rightX = leftX + clip.DurSec * px;
            if (Math.Abs(pos.X - leftX) <= TrimEdgePx)
                return EdgeKind.Left;
            if (Math.Abs(pos.X - rightX) <= TrimEdgePx)
                return EdgeKind.Right;

            var top = TrackTop(Editor!.Document.Tracks.IndexOf(track)) + TimelineGeometry.ClipInset;
            if (pos.Y > top + TimelineGeometry.ClipTotal(track) / 2)
                return EdgeKind.None;

            // an existing fade's tip, wherever it is
            if (clip.FadeInSec > 1e-6 && Math.Abs(pos.X - (leftX + clip.FadeInSec * px)) <= FadeZonePx * 0.5)
                return EdgeKind.FadeIn;
            if (clip.FadeOutSec > 1e-6 && Math.Abs(pos.X - (rightX - clip.FadeOutSec * px)) <= FadeZonePx * 0.5)
                return EdgeKind.FadeOut;

            // too narrow for fade zones: trim only
            if (clip.DurSec * px <= (TrimEdgePx + FadeZonePx) * 2 + 4)
                return EdgeKind.None;
            if (pos.X > leftX + TrimEdgePx && pos.X <= leftX + TrimEdgePx + FadeZonePx)
                return EdgeKind.FadeIn;
            if (pos.X < rightX - TrimEdgePx && pos.X >= rightX - TrimEdgePx - FadeZonePx)
                return EdgeKind.FadeOut;
            return EdgeKind.None;
        }

        private Track? HitTestTrack(double y)
        {
            if (Editor is null)
                return null;
            // iterate by list position so hit-testing matches rendering, regardless of stale Index values
            for (var i = 0; i < Editor.Document.Tracks.Count; i++)
            {
                var track = Editor.Document.Tracks[i];
                if (y >= TrackTop(i) && y < TrackBottom(i))
                    return track;
            }
            return null;
        }

        private Clip? HitTestClip(Track track, double time, double pointerX)
        {
            return track.Clips.FirstOrDefault(c =>
                time >= c.StartSec && time < c.StartSec + c.DurSec);
        }

        /// <summary>
        /// The clip actually drawn under a point. Anywhere else in a lane (above or below the clip, in the gaps)
        /// is empty space: a clip does not own the whole column of its lane.
        /// </summary>
        private Clip? ClipAt(Point pos, out Track? track)
        {
            track = HitTestTrack(pos.Y);
            if (Editor is null || track is null || pos.X <= TrackHeaderWidth)
                return null;
            var clip = HitTestClip(track, XToTime(pos.X), pos.X);
            if (clip is null)
                return null;
            var top = TrackTop(Editor.Document.Tracks.IndexOf(track)) + TimelineGeometry.ClipInset;
            return pos.Y >= top && pos.Y <= top + TimelineGeometry.ClipTotal(track) ? clip : null;
        }

        private enum RangeGrab { None, Start, End, Body }

        /// <summary>True when a clip has a neighbor touching it on the left/right in the same track.</summary>
        private static (bool HasPrev, bool HasNext) HasAdjacent(Track track, Clip clip)
        {
            const double eps = 1e-6;
            var hasPrev = false;
            var hasNext = false;
            foreach (var other in track.Clips)
            {
                if (other.Id == clip.Id)
                    continue;
                if (Math.Abs(other.StartSec + other.DurSec - clip.StartSec) < eps)
                    hasPrev = true;
                if (Math.Abs(other.StartSec - (clip.StartSec + clip.DurSec)) < eps)
                    hasNext = true;
            }
            return (hasPrev, hasNext);
        }

        /// <summary>Returns whether the pointer is over a clip's trim or fade edge.</summary>
        private EdgeKind HoverClipEdge(Point pos)
            => ClipAt(pos, out var track) is { } clip && track is not null ? EdgeAt(clip, track, pos) : EdgeKind.None;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Editor is null)
                return;

            if (e.Key == Key.Escape && _copyDrag is not null)
            {
                CancelCopyDrag();
                e.Handled = true;
                return;
            }

            // M = add a marker at the playhead (selected clip / active track / timeline)
            if (e.Key == Key.M && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _editorVm?.AddMarkerAtPlayheadCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.A && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta))
            {
                // with only markers selected, Ctrl+A means every marker; otherwise everything on the timeline
                if (Editor.Selection.Items is { Count: > 0 } items && items.All(k => k.Group == SelectionGroup.Marker))
                    Editor.Selection.SelectMarkers(Editor.AllMarkers().Select(marker => marker.Marker.Id));
                else
                    SelectAll();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ClearSelection();
                e.Handled = true;
                return;
            }

            // N = turn magnetic snapping on/off
            if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.None)
            {
                _editorVm?.ToggleMagneticSnapCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                // whatever is selected knows how to be deleted
                if (_editorVm?.DeleteSelection() == true || (_editorVm is null && Editor.DeleteSelection() is not null))
                {
                    InvalidateMeasure();
                    InvalidateVisual();
                    e.Handled = true;
                }
            }
        }

        // ---- rendering ----

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(SurfaceBackground, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

            if (Editor is null)
                return;

            var px = Viewport.PixelsPerSecond;
            var scroll = Viewport.ScrollTime;

            // clip everything below the ruler + right of the header so clips never
            // draw over the header column when zoomed/scrolled
            var clipArea = new Rect(TrackHeaderWidth, RulerHeight,
                Math.Max(0, Bounds.Width - TrackHeaderWidth), Math.Max(0, Bounds.Height - RulerHeight));

            for (var i = 0; i < Editor.Document.Tracks.Count; i++)
            {
                var track = Editor.Document.Tracks[i];
                var top = TrackTop(i);
                var height = TrackHeight(track);

                var isTrackSelected = track.Id == _selectedTrackId;
                var rowRect = new Rect(0, top, Bounds.Width, height);
                var laneBrush = i % 2 == 0 ? TrackLaneBrush : TrackLaneAltBrush;
                context.DrawRectangle(laneBrush, null, rowRect);
                context.DrawRectangle(TrackHeaderBrush, null, new Rect(0, top, TrackHeaderWidth, height));
                if (isTrackSelected)
                {
                    context.DrawRectangle(SelectedLaneTintBrush, null, rowRect);
                    context.DrawRectangle(SelectionBrush, null, new Rect(0, top, TrackAccentBarWidth, height));
                }
                context.DrawLine(new Pen(BorderBrush, 1), new Point(TrackHeaderWidth, top), new Point(TrackHeaderWidth, top + height));
                context.DrawLine(new Pen(BorderBrush, 1), new Point(0, top + height), new Point(Bounds.Width, top + height));
                DrawTrackGrid(context, top, height);

                var trackLabel = track.Name ?? track.Kind.ToString();
                var trackText = new FormattedText(
                    trackLabel,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    11,
                    TrackHeaderTextBrush);
                context.DrawText(trackText, new Point(HeaderLabelX(), top + (height - trackText.Height) / 2));

                DrawHeaderToggle(context, track, HeaderButtonRect(track, HeaderToggleColumn));
                DrawRackToggle(context, track);
                var deleteHovered = _hoverHeaderButton is (string tid, int col)
                    && tid == track.Id && col == HeaderDeleteColumn;
                if (isTrackSelected || deleteHovered)
                    DrawHeaderDelete(context, track, HeaderButtonRect(track, HeaderDeleteColumn));

                // clip area, offset by header + viewport scroll
                using (context.PushClip(clipArea))
                {
                    DrawTrackMarkers(context, track, top, height);
                    var trackTransitions = Editor.EnumerateTransitions(track).ToList();

                    foreach (var clip in track.Clips)
                    {
                        var x = TrackHeaderWidth + Viewport.TimeToX(clip.StartSec + GetRippleOffsetSec(clip.Id));
                        var w = clip.DurSec * px;
                        if (x + w < TrackHeaderWidth || x > Bounds.Width)
                            continue;   // culled: not visible

                        var (fill, border) = clip.Kind switch
                        {
                            ClipKind.Video => (VideoBrush, VideoBorder),
                            ClipKind.Audio => (AudioBrush, AudioBorder),
                            ClipKind.Text => (TextClipBrush, TextBorder),
                            ClipKind.Image => (ImageClipBrush, ImageBorder),
                            _ => (VideoBrush, VideoBorder),
                        };

                        // clip widget = label strip (name) on top + body (filmstrip/waveform).
                        // top inset leaves room for marker tabs inside the lane.
                        var clipTop = top + TimelineGeometry.ClipInset;
                        var totalHeight = TimelineGeometry.ClipTotal(track);
                        // a text clip has no name strip: its words are written inside the bar instead
                        var labelHeight = clip is TextClip ? 0 : TimelineGeometry.LabelHeight(track);
                        var bodyHeight = totalHeight - labelHeight;

                        // square the corners on edges shared with an adjacent clip so split
                        // halves butt cleanly instead of showing overlapping rounded corners
                        var (hasPrev, hasNext) = HasAdjacent(track, clip);
                        var topLeft = hasPrev ? 0 : ClipCornerRadius;
                        var topRight = hasNext ? 0 : ClipCornerRadius;
                        var bottomLeft = hasPrev ? 0 : ClipCornerRadius;
                        var bottomRight = hasNext ? 0 : ClipCornerRadius;
                        var corner = new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);

                        var widgetRect = new Rect(x, clipTop, w, totalHeight);

                        // only the first clip of a contiguous run casts a shadow, so split
                        // halves don't stack shadows and read as overlapping
                        if (!hasPrev)
                        {
                            var shadowRect = new RoundedRect(widgetRect.Translate(new Vector(0, 1)), corner);
                            context.DrawRectangle(ClipShadow, null, shadowRect);
                        }

                        var isClipSelected = _selectedClipId is not null
                            && Editor.Selection.IsSelected(clip.Id);
                        var isEffectDropTarget = _dropEffectClipId == clip.Id;
                        var outline = isEffectDropTarget
                            ? SelectionBrush
                            : isClipSelected ? ClipSelectionBrush : border;
                        var outlineWidth = isEffectDropTarget ? 3 : (isClipSelected ? 2 : 1);

                        // --- name strip above the clip body ---
                        if (labelHeight > 0)
                        {
                            var labelRect = new Rect(x, clipTop, w, labelHeight);
                            var labelBrush = fill;
                            context.DrawRectangle(labelBrush, null, new RoundedRect(labelRect, new CornerRadius(topLeft, topRight, 0, 0)));

                            using (context.PushClip(labelRect))
                            {
                                var clipName = ClipName(clip);
                                if (clipName is not null)
                                {
                                    var labelText = new FormattedText(
                                        clipName,
                                        System.Globalization.CultureInfo.CurrentCulture,
                                        FlowDirection.LeftToRight,
                                        Typeface.Default,
                                        10,
                                        Brushes.White);
                                    context.DrawText(labelText, new Point(x + 4 + (w >= 54 && BuildRack(clip).Count > 0 ? 26 : 0),
                                        labelRect.Y + (labelHeight - labelText.Height) / 2));
                                }
                            }
                        }

                        // --- clip body ---
                        var rect = new Rect(x, clipTop + labelHeight, w, bodyHeight);
                        // under a name strip the body's top is square, so the two read as one rounded bar
                        var bodyRounded = new RoundedRect(rect, labelHeight > 0
                            ? new CornerRadius(0, 0, bottomRight, bottomLeft)
                            : corner);

                        // draw filmstrip as video clip background
                        if (clip is VideoClip vc && MediaById is not null
                            && MediaById.TryGetValue(vc.SourceId, out var asset)
                            && asset?.Filmstrip is string stripPath
                            && GetBitmap(stripPath) is Bitmap strip)
                        {
                            DrawFilmstrip(context, rect, vc, asset, strip);
                            context.DrawRectangle(Brushes.Transparent, new Pen(outline, outlineWidth), bodyRounded);
                        }
                        // draw waveform as audio clip background
                        else if (clip is AudioClip ac && MediaById is not null
                            && MediaById.TryGetValue(ac.SourceId, out var audioAsset)
                            && audioAsset is not null)
                        {
                            DrawAudioWaveform(context, rect, ac, audioAsset);
                            context.DrawRectangle(Brushes.Transparent, new Pen(outline, outlineWidth), bodyRounded);
                        }
                        // a still repeats its thumbnail along the bar
                        else if (clip is ImageClip image && MediaById is not null
                            && MediaById.TryGetValue(image.SourceId, out var imageAsset)
                            && imageAsset?.Thumbnail is string thumbPath
                            && GetBitmap(thumbPath) is Bitmap thumb && thumb.PixelSize.Height > 0)
                        {
                            context.DrawRectangle(fill, null, bodyRounded);
                            using (context.PushClip(bodyRounded))
                            {
                                var tileW = rect.Height * thumb.PixelSize.Width / thumb.PixelSize.Height;
                                for (var tx = rect.X; tx < rect.Right && tileW > 1; tx += tileW)
                                    context.DrawImage(thumb, new Rect(0, 0, thumb.PixelSize.Width, thumb.PixelSize.Height),
                                        new Rect(tx, rect.Y, tileW, rect.Height));
                            }
                            context.DrawRectangle(Brushes.Transparent, new Pen(outline, outlineWidth), bodyRounded);
                        }
                        else
                        {
                            context.DrawRectangle(fill, new Pen(outline, outlineWidth), bodyRounded);
                        }

                        if (clip is TextClip textClip)
                            DrawTextClipWords(context, rect, textClip);

                        if (isEffectDropTarget)
                        {
                            context.DrawRectangle(
                                new SolidColorBrush(Color.FromArgb(50, 0x4d, 0xa3, 0xff)),
                                null, new RoundedRect(widgetRect, corner));
                        }

                        DrawFadeRamps(context, rect, clip);

                        foreach (var t in trackTransitions)
                        {
                            if (t.RightClipId == clip.Id)
                                DrawTransitionSpan(context, rect, t, isLeftClip: false, px);
                            if (t.LeftClipId == clip.Id)
                            {
                                DrawTransitionSpan(context, rect, t, isLeftClip: true, px);
                                DrawTransitionBadge(context, t, rect.Y, rect.Bottom);
                            }
                        }

                        DrawClipMarkers(context, x, clipTop, clip, track);
                        DrawKeyframeDiamonds(context, clip, rect, px);
                        DrawRack(context, clip, x, w, clipTop, track);

                        if (!clip.Enabled)
                        {
                            context.DrawRectangle(
                                new SolidColorBrush(Color.FromArgb(120, 0x10, 0x10, 0x12)),
                                null, new RoundedRect(widgetRect, corner));
                        }

                        // resize-handle indicator on the edge under the cursor (or both edges when selected)
                        var hoverLeft = _hoverEdge == EdgeKind.Left;
                        var hoverRight = _hoverEdge == EdgeKind.Right;
                        var hoverFadeIn = _hoverEdge == EdgeKind.FadeIn;
                        var hoverFadeOut = _hoverEdge == EdgeKind.FadeOut;
                        if (isClipSelected || hoverLeft)
                            DrawResizeHandle(context, x, rect.Y, rect.Height);
                        if (isClipSelected || hoverRight)
                            DrawResizeHandle(context, x + w, rect.Y, rect.Height);
                        if (hoverFadeIn || (isClipSelected && clip.FadeInSec > 1e-6))
                            DrawResizeHandle(context, x + Math.Min(clip.FadeInSec, clip.DurSec) * px, rect.Y, rect.Height);
                        if (hoverFadeOut || (isClipSelected && clip.FadeOutSec > 1e-6))
                            DrawResizeHandle(context, x + w - Math.Min(clip.FadeOutSec, clip.DurSec) * px, rect.Y, rect.Height);
                    }   // foreach clip
                    DrawRack(context, track, TrackHeaderWidth + Viewport.TimeToX(0),
                        track.DurSec * Viewport.PixelsPerSecond, top + TimelineGeometry.ClipInset, track);
                }   // PushClip(clipArea)
            }   // for tracks

            // add-track button at bottom of header column
            if (Editor.Document.Tracks.Count > 0)
            {
                var addRect = AddTrackButtonRect();
                DrawHeaderAdd(context, addRect);
            }

            // drop preview: highlight target track + ghost clip rect
            if (_dropPreviewTrack is not null && _dropPreviewTime >= 0 && _dropPreviewAsset is not null)
            {
                var trackIndex = Editor.Document.Tracks.IndexOf(_dropPreviewTrack);
                var trackTop = TrackTop(Math.Max(0, trackIndex));
                var trackHeight = TrackHeight(_dropPreviewTrack);
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#2244aa44")), null,
                    new Rect(0, trackTop, Bounds.Width, trackHeight));

                var ghostX = TrackHeaderWidth + Viewport.TimeToX(_dropPreviewTime);
                var ghostW = _dropPreviewAsset.DurationSec * px;
                var ghostHeight = Math.Min(TimelineGeometry.ClipTotal(_dropPreviewTrack), trackHeight - 8);
                var ghostRect = new Rect(ghostX, trackTop + TimelineGeometry.ClipInset, ghostW, ghostHeight);
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#88ffffff")), null,
                    new RoundedRect(ghostRect, ClipCornerRadius));
                context.DrawRectangle(Brushes.Transparent, new Pen(SelectionBrush, 2),
                    new RoundedRect(ghostRect, ClipCornerRadius));
            }

            if (_dropPreviewTrack is not null && _dropPreviewAsset is null && _dropPreviewTime >= 0)
            {
                var trackTop = TrackTop(Editor.Document.Tracks.IndexOf(_dropPreviewTrack));
                var height = TrackHeight(_dropPreviewTrack);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 199, 154, 61)), null,
                    new Rect(TrackHeaderWidth, trackTop, Math.Max(0, Bounds.Width - TrackHeaderWidth), height));
                var ghostX = TrackHeaderWidth + Viewport.TimeToX(_dropPreviewTime);
                var ghostW = Math.Max(4, _dropEffectDuration * px);
                var ghost = _dropLayer
                    ? new Rect(ghostX, trackTop + TimelineGeometry.ClipInset, ghostW,
                        Math.Min(TimelineGeometry.ClipTotal(_dropPreviewTrack), height - 8))
                    : new Rect(ghostX, trackTop + TimelineGeometry.ClipInset + TimelineGeometry.ClipTotal(_dropPreviewTrack) + 4,
                        ghostW, 24);
                context.DrawRectangle(_dropLayer ? TextClipBrush : new SolidColorBrush(Color.FromArgb(140, 199, 154, 61)),
                    new Pen(Brushes.White, 1), ghost, 3, 3);
            }

            // drop indicator line (media into empty space)
            if (_dropTimeSec >= 0 && _dropPreviewTrack is null)
            {
                var x = TrackHeaderWidth + Viewport.TimeToX(_dropTimeSec);
                context.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(x, RulerHeight), new Point(x, Bounds.Height));
            }

            // transition drop: highlight cut between abutting clips
            if (_dropTransitionCutSec >= 0 && _dropTransitionTrack is not null)
            {
                var listIndex = Editor.Document.Tracks.IndexOf(_dropTransitionTrack);
                if (listIndex >= 0)
                {
                    var trackTop = TrackTop(listIndex);
                    var x = TrackHeaderWidth + Viewport.TimeToX(_dropTransitionCutSec);
                    var marker = new SolidColorBrush(Color.FromArgb(230, 0xea, 0xb3, 0x08));
                    var cutTotal = TimelineGeometry.ClipTotal(_dropTransitionTrack);
                    context.DrawLine(new Pen(marker, 3), new Point(x, trackTop + TimelineGeometry.ClipInset),
                        new Point(x, trackTop + TimelineGeometry.ClipInset + cutTotal));
                    var midY = trackTop + TimelineGeometry.ClipInset + cutTotal / 2;
                    var diamond = new StreamGeometry();
                    using (var gc = diamond.Open())
                    {
                        gc.BeginFigure(new Point(x, midY - 6), true);
                        gc.LineTo(new Point(x + 5, midY));
                        gc.LineTo(new Point(x, midY + 6));
                        gc.LineTo(new Point(x - 5, midY));
                        gc.EndFigure(true);
                    }
                    context.DrawGeometry(marker, null, diamond);
                }
            }

            //DrawMarkerGuides(context);

            // the ruler and marker lane are pinned to the top of the pane and cover the tracks scrolling under them
            var pinned = StickyOffset;
            using (context.PushTransform(Matrix.CreateTranslation(0, pinned)))
            {
                DrawRuler(context, px, scroll);
                DrawTimelineMarkers(context);
            }

            // playhead
            DrawEmptyHint(context);
            DrawSnapGuide(context);
            DrawMarquee(context);
            DrawCopyPreview(context);

            var playheadX = TrackHeaderWidth + Viewport.TimeToX(PlayheadTimeSec);
            if (playheadX >= TrackHeaderWidth)
            {
                var playheadPen = new Pen(EditorTheme.PlayheadBrush, 1);
                // the line starts at the cap's lower tip, not behind it
                context.DrawLine(playheadPen, new Point(playheadX, pinned + MarkerLaneHeight + PlayheadCapHeight),
                    new Point(playheadX, Bounds.Height));
                var cap = new StreamGeometry();
                using (var gc = cap.Open())
                {
                    // the cap rides with the pinned ruler
                    gc.BeginFigure(new Point(playheadX - 5, pinned + MarkerLaneHeight), true);
                    gc.LineTo(new Point(playheadX + 5, pinned + MarkerLaneHeight));
                    gc.LineTo(new Point(playheadX, pinned + MarkerLaneHeight + PlayheadCapHeight));
                    gc.EndFigure(true);
                }
                context.DrawGeometry(EditorTheme.PlayheadBrush, null, cap);
            }
        }
    }
}
