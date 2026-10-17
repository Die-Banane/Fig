using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        /// <summary>
        /// Adds a text clip at <paramref name="startSec"/>, on <paramref name="trackId"/> when it has room there,
        /// otherwise on the first video track that does, or a new one. One undo step (including any new track);
        /// the new clip is selected.
        /// </summary>
        public TextClip AddText(string? trackId, double startSec, string text = "Title", TextStyle? style = null,
            double durSec = 3, LayerTransform? layout = null)
        {
            var clip = new TextClip
            {
                Text = text,
                Style = style ?? TextStyle.Default,
                Layout = layout ?? LayerTransform.Default,
                StartSec = Math.Max(0, FrameMath.SnapToFrame(startSec, Document.Rate)),
                DurSec = Math.Max(0.1, durSec),
            };
            PlaceLayer(trackId, clip);
            return clip;
        }

        /// <summary>Adds a library title (<see cref="TextPresets"/>) at <paramref name="startSec"/>, as <see cref="AddText(string?, double, string, TextStyle?, double, LayerTransform?)"/>.</summary>
        public TextClip AddText(string? trackId, double startSec, TextPreset preset)
            => AddText(trackId, startSec, preset.Text, preset.Style, preset.DurSec, preset.Layout);

        /// <summary>Places a drawn layer (text, image) on a video track as one undo step and selects it.</summary>
        private void PlaceLayer(string? trackId, Clip clip)
        {
            bool Fits(Track t) => t.Kind == TrackKind.Video && !WouldOverlap(t.Id, clip.StartSec, clip.DurSec);

            var commands = new List<IEditCommand>();
            var track = (trackId is null ? null : FindTrack(trackId)) is { } wanted && Fits(wanted)
                ? wanted
                : Document.Tracks.FirstOrDefault(Fits);
            if (track is null)
            {
                track = new Track { Kind = TrackKind.Video, Name = $"V{CountTracks(TrackKind.Video) + 1}" };
                var before = Document.Tracks.ToList();
                commands.Add(new SetTrackListCommand(this, "Add track", before, WithNewTrack(before, track)));
            }
            commands.Add(new AddClipCommand(this, track.Id, clip));
            History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands.ToArray()));
            RefreshTrackIndices();
            Selection.SelectOnly(clip.Id);
            RaiseChanged();
        }

        /// <summary>
        /// The clips an inspector edit of <paramref name="facet"/> reaches: each listed clip, and the clips linked
        /// to it, that have the facet (fading a video also fades its linked audio).
        /// </summary>
        public IReadOnlyList<string> EditTargets(IEnumerable<string> clipIds, Facet facet)
            => clipIds.SelectMany(LinkGroup).Where(c => c.Facets.HasFlag(facet)).Select(c => c.Id).Distinct().ToList();

        /// <summary>
        /// Sets one value on every listed clip that has it, as one undo step (a slider drag coalesces).
        /// <paramref name="property"/> names the edit for the undo history and for coalescing.
        /// </summary>
        public void SetClipValue<T>(IEnumerable<string> clipIds, string property, Func<Clip, T> get, Action<Clip, T> set, T value)
        {
            var ids = clipIds.Where(id => FindClip(id) is not null).ToList();
            if (ids.Count == 0 || ids.All(id => EqualityComparer<T>.Default.Equals(get(FindClip(id)!), value)))
                return;
            History.ExecuteCoalescing(new SetClipValueCommand<T>(this, ids, property, get, set, value));
            RaiseChanged();
        }

        /// <summary>Gives each listed layer its own layout, as one undo step (a gizmo drag coalesces into it).</summary>
        public void SetLayouts(IReadOnlyDictionary<string, LayerTransform> layouts)
        {
            var changed = layouts.Where(kv => FindClip(kv.Key) is ILayerClip layer && layer.Layout != kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (changed.Count == 0)
                return;
            History.ExecuteCoalescing(new SetClipValueCommand<LayerTransform>(this, layouts, "layout",
                c => ((ILayerClip)c).Layout, (c, v) => ((ILayerClip)c).Layout = v));
            RaiseChanged();
        }

        /// <summary>
        /// The topmost drawn layer (text, image) showing at <paramref name="timeSec"/> under the frame point
        /// (<paramref name="x"/>, <paramref name="y"/>, 0..1), or null. Higher tracks draw over lower ones.
        /// </summary>
        public Clip? LayerAt(double timeSec, double x, double y, double aspect, Func<Clip, LayerSize?> measure)
        {
            foreach (var (_, track) in TrackStacking.TopDown(Document))
            {
                foreach (var clip in track.Clips)
                    if (clip is ILayerClip layer && clip.Enabled
                        && timeSec >= clip.StartSec && timeSec < clip.StartSec + clip.DurSec
                        && measure(clip) is { } size && LayerGeometry.Contains(layer.Layout, size, x, y, aspect))
                        return clip;
            }
            return null;
        }
    }
}
