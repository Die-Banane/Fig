using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Fig.App.Services;
using Fig.Core.Timeline;

namespace Fig.App.Views
{
    /// <summary>
    /// Dragging a track's header up or down moves the track. A line shows where it will land; letting go moves it
    /// there as one undo step. Higher video tracks cover lower ones, so this is how the picture's layers are reordered.
    /// </summary>
    public partial class TimelineView
    {
        private sealed record TrackDrag(string TrackId, double PressY, bool Active = false, int InsertIndex = -1);

        private TrackDrag? _trackDrag;

        private void ArmTrackDrag(Track track, Point pos, IPointer pointer)
        {
            _trackDrag = new TrackDrag(track.Id, pos.Y);
            pointer.Capture(this);
        }

        /// <summary>Where a track dropped at <paramref name="y"/> goes: before the track whose upper half it is in.</summary>
        private int TrackInsertIndexAt(double y)
        {
            if (Editor is null)
                return 0;
            for (var i = 0; i < Editor.Document.Tracks.Count; i++)
                if (y < TrackTop(i) + TrackHeight(Editor.Document.Tracks[i]) / 2)
                    return i;
            return Editor.Document.Tracks.Count;
        }

        private bool UpdateTrackDrag(Point pos)
        {
            if (_trackDrag is not { } drag)
                return false;
            var active = drag.Active || Math.Abs(pos.Y - drag.PressY) >= MarqueeStartPx;
            _trackDrag = drag with { Active = active, InsertIndex = active ? TrackInsertIndexAt(pos.Y) : -1 };
            if (active)
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            InvalidateVisual();
            return true;
        }

        private bool EndTrackDrag(PointerReleasedEventArgs e)
        {
            if (_trackDrag is not { } drag)
                return false;
            _trackDrag = null;
            if (e.Pointer.Captured == this)
                e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            if (drag.Active && drag.InsertIndex >= 0 && Editor is not null)
            {
                Editor.MoveTrack(drag.TrackId, drag.InsertIndex);
                InvalidateMeasure();
            }
            InvalidateVisual();
            return true;
        }

        /// <summary>The line where a dragged track will land.</summary>
        private void DrawTrackDrop(DrawingContext context)
        {
            if (_trackDrag is not { Active: true, InsertIndex: >= 0 } drag || Editor is null)
                return;
            var count = Editor.Document.Tracks.Count;
            var y = drag.InsertIndex < count ? TrackTop(drag.InsertIndex) : TrackBottom(count - 1);
            context.DrawRectangle(EditorTheme.AccentBrush, null, new Rect(0, y - 1.5, Bounds.Width, 3));
            var index = Editor.Document.Tracks.FindIndex(t => t.Id == drag.TrackId);
            if (index >= 0)
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null,
                    new Rect(0, TrackTop(index), TrackHeaderWidth, TrackHeight(Editor.Document.Tracks[index])));
        }
    }
}
