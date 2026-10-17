using System;
using System.Collections.Generic;
using System.Linq;
using Fig.Core.Media;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    public partial class TimelineEditor
    {
        public Timeline Document { get; }
        public CommandHistory History { get; }
        public SelectionState Selection { get; }
        public Func<string, MediaAsset?>? MediaResolver { get; set; }
        public string? TransitionValidationMessage { get; private set; }

        public event Action? TimelineChanged;

        /// <param name="undoSteps">How many edits can be undone before the oldest are forgotten.</param>
        public TimelineEditor(Timeline document, int undoSteps = 100)
        {
            Document = document;
            History = new CommandHistory(undoSteps);
            Selection = new SelectionState();
        }

        public IReadOnlyList<Clip> Cut(string clipId, double atSec)
        {
            var group = LinkGroup(clipId)
                .Where(c => c.StartSec < atSec && atSec < c.StartSec + c.DurSec)
                .ToList();
            if (group.Count == 0)
            {
                var cmd = new CutCommand(this, clipId, atSec);
                History.Execute(cmd);
                RaiseChanged();
                return cmd.ProducedClips;
            }

            // one fresh group id for the right halves of the whole group
            var secondGroup = group[0].LinkGroupId is null ? null : Guid.NewGuid().ToString("N");
            var commands = group
                .Select(c => (IEditCommand)new CutCommand(this, c.Id, atSec, secondGroup))
                .ToArray();

            History.Execute(commands.Length == 1 ? commands[0] : new CompositeCommand(commands));
            RaiseChanged();
            return commands.Cast<CutCommand>().SelectMany(c => c.ProducedClips).ToList();
        }

        public void Trim(string clipId, double newIn, double newOut)
        {
            History.Execute(new TrimCommand(this, clipId, newIn, newOut));
            RaiseChanged();
        }

        /// <summary>Trims the clip's in/out range, applying the same trim to its linked group.</summary>
        public void TrimLinked(string clipId, double newIn, double newOut)
        {
            var commands = LinkGroup(clipId)
                .Select(c => (IEditCommand)new TrimCommand(this, c.Id, newIn, newOut))
                .ToArray();
            if (commands.Length == 0)
                return;
            History.Execute(commands.Length == 1 ? commands[0] : new CompositeCommand(commands));
            RaiseChanged();
        }

        public void Move(string clipId, double newStartSec)
        {
            var clip = FindClip(clipId) ?? throw new InvalidOperationException($"Clip '{clipId}' not found");
            History.Execute(new MoveCommand(this, clipId, newStartSec, clip.StartSec));
            RaiseChanged();
        }

        public void RippleDelete(string clipId)
        {
            History.Execute(new RippleDeleteCommand(this, clipId));
            RaiseChanged();
        }

        /// <summary>
        /// Ripple-deletes every distinct selected link group. Unrelated clips that merely
        /// share a timeline position are not touched.
        /// </summary>
        public void RippleDeleteSelected()
        {
            var seeds = SelectedGroupSeeds();
            if (seeds.Count == 0)
                return;
            if (seeds.Count == 1)
            {
                RippleDelete(seeds[0]);
                Selection.Clear();
                return;
            }

            var commands = seeds.Select(id => (IEditCommand)new RippleDeleteCommand(this, id)).ToArray();
            History.Execute(new CompositeCommand(commands));
            Selection.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// Lifts every distinct selected link group, leaving gaps. Does not affect
        /// unselected clips even if they share the same timeline range.
        /// </summary>
        public void LiftSelected()
        {
            var seeds = SelectedGroupSeeds();
            if (seeds.Count == 0)
                return;
            if (seeds.Count == 1)
            {
                Lift(seeds[0]);
                Selection.Clear();
                return;
            }

            var commands = seeds.Select(id => (IEditCommand)new LiftCommand(this, id)).ToArray();
            History.Execute(new CompositeCommand(commands));
            Selection.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// One representative clip id per selected link group (so a selected video+audio
        /// pair is operated on once, not twice).
        /// </summary>
        private List<string> SelectedGroupSeeds()
        {
            var seeds = new List<string>();
            var seen = new HashSet<string>();
            foreach (var id in Selection.SelectedClipIds)
            {
                var clip = FindClip(id);
                if (clip is null)
                    continue;
                var key = string.IsNullOrEmpty(clip.LinkGroupId) ? clip.Id : clip.LinkGroupId!;
                if (seen.Add(key))
                    seeds.Add(clip.Id);
            }
            return seeds;
        }

        public void AddClip(string trackId, Clip clip)
        {
            var track = FindTrack(trackId) ?? throw new InvalidOperationException($"Track '{trackId}' not found");
            InsertClip(track, clip);
            RaiseChanged();
        }

        /// <summary>
        /// Adds a media asset to the timeline as a clip. A video's sound is part of its clip (see
        /// <see cref="VideoClip.PlaysAudio"/>) until it is detached.
        /// Returns null only when the drop would overlap an existing clip on the targeted
        /// track of the matching kind.
        /// </summary>
        public Clip? AddMediaLinked(MediaAsset asset, string targetTrackId, double startSec)
        {
            var target = FindTrack(targetTrackId)
                ?? throw new InvalidOperationException($"Track '{targetTrackId}' not found");

            var clip = CreateClipFromAsset(asset);
            PreparePlacement(clip, asset);
            clip.StartSec = FrameMath.SnapToFrame(startSec, Document.Rate);
            ClipFactory.SetSourceRange(clip, 0, asset.DurationSec);

            var desiredKind = asset.Kind == MediaKind.Audio ? TrackKind.Audio : TrackKind.Video;

            // drop on a matching-kind track: honor that target and reject on overlap.
            // drop on the wrong kind (e.g. video onto an audio lane): place on a free
            // matching track instead of inserting the wrong clip type.
            Track placeTrack;
            if (target.Kind == desiredKind)
            {
                if (WouldOverlap(target.Id, clip.StartSec, clip.DurSec))
                    return null;
                placeTrack = target;
            }
            else
            {
                placeTrack = FindFreeTrack(desiredKind, clip, current: null)!;
            }

            InsertClip(placeTrack, clip);
            RaiseChanged();
            return clip;
        }

        /// <summary>
        /// Adds a media asset to a brand-new track of its kind. Used when dropping into empty space so
        /// the new clip gets its own track instead of sharing the one above.
        /// </summary>
        public Clip AddMediaNewTracks(MediaAsset asset, double startSec)
        {
            var clip = CreateClipFromAsset(asset);
            PreparePlacement(clip, asset);
            clip.StartSec = FrameMath.SnapToFrame(startSec, Document.Rate);
            ClipFactory.SetSourceRange(clip, 0, asset.DurationSec);

            if (asset.Kind == MediaKind.Audio)
            {
                var audioTrack = AddTrack(TrackKind.Audio);
                InsertClip(audioTrack, clip);
                RaiseChanged();
                return clip;
            }

            var videoTrack = AddTrack(TrackKind.Video);

            InsertClip(videoTrack, clip);
            RaiseChanged();
            return clip;
        }

        /// <summary>Returns all clips sharing the given clip's link group (including itself).</summary>
        public IReadOnlyList<Clip> LinkGroup(string clipId)
        {
            var clip = FindClip(clipId);
            if (clip is null)
                return Array.Empty<Clip>();
            // treat empty the same as null so unlinked clips never form a phantom group
            if (string.IsNullOrEmpty(clip.LinkGroupId))
                return new[] { clip };

            var group = new List<Clip>();
            foreach (var track in Document.Tracks)
                foreach (var c in track.Clips)
                    if (c.LinkGroupId == clip.LinkGroupId)
                        group.Add(c);
            return group;
        }

        public static Clip CreateClipFromAsset(MediaAsset asset)
        {
            return asset.Kind switch
            {
                MediaKind.Audio => new AudioClip { SourceId = asset.Id, DurSec = asset.DurationSec, SourceRate = asset.SourceRate },
                // a still lasts the default length; an animated one (a GIF) at least one whole loop
                MediaKind.Image => new ImageClip { SourceId = asset.Id, DurSec = Math.Max(ImageClip.DefaultDurationSec, asset.DurationSec) },
                _ => new VideoClip { SourceId = asset.Id, DurSec = asset.DurationSec, SourceRate = asset.SourceRate, PlaysAudio = asset.HasAudio },
            };
        }

        /// <summary>
        /// Readies a newly created clip for the timeline. An empty timeline whose rate the user hasn't chosen takes
        /// the media's frame rate, if it reports a sensible one. The clip plays in real time, as long as its file,
        /// whatever its frame rate.
        /// </summary>
        private Clip PreparePlacement(Clip clip, MediaAsset asset)
        {
            clip.SourceRate = asset.SourceRate;
            if (!Document.RateChosen && HasNoClips() && FrameRate.Plausible(asset.SourceRate) is { } rate)
                Document.Rate = rate;
            return clip;
        }

        /// <summary>Sets the project frame rate, one undo step. Clips keep their place and length.</summary>
        public void SetRate(FrameRate rate)
        {
            if (rate.Equals(Document.Rate) && Document.RateChosen)
                return;
            History.Execute(new SetTimelineRateCommand(this, rate));
            RaiseChanged();
        }

        private bool HasNoClips()
        {
            foreach (var track in Document.Tracks)
                if (track.Clips.Count > 0)
                    return false;
            return true;
        }

        public void AddClip(Clip clip)
        {
            var track = FindTrackForClip(clip);
            InsertClip(track, clip);
            RaiseChanged();
        }

        /// <summary>
        /// Moves a clip (and its linked partner) onto the track under the cursor, keeping the
        /// same timeline position. The primary clip goes to <paramref name="targetTrackId"/>;
        /// each linked member goes to a track of its own kind (video→video, audio→audio), creating
        /// one if none is free. Refuses if the move would overlap any clip. Returns true on success.
        /// </summary>
        /// <summary>A clip's place, extent, source range, speed curve and track, for a drag's undo step.</summary>
        public ClipGeometry CaptureGeometry(Clip clip) => ClipGeometry.Capture(clip) with { TrackId = FindClipTrack(clip.Id)?.Id };

        /// <summary>
        /// Moves dragged clips live: those of the target's kind (all from one track) onto <paramref name="targetTrackId"/>,
        /// every clip to its start in <paramref name="starts"/>. Clips of the other kind (a linked partner) stay on their
        /// own tracks and only move in time. Nothing changes, and false comes back, when anything would land on another
        /// clip. Not an undo step by itself: the drag records one when it ends (<see cref="CommitClipGeometry"/>).
        /// </summary>
        public bool MoveClipsToTrack(IReadOnlyCollection<string> clipIds, string targetTrackId, IReadOnlyDictionary<string, double> starts)
        {
            if (FindTrack(targetTrackId) is not { } target)
                return false;
            var clips = clipIds.Distinct().Select(FindClip).Where(c => c is not null).Cast<Clip>().ToList();
            var movers = clips.Where(c => (c.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Video) == target.Kind).ToList();
            if (movers.Count == 0 || movers.Select(c => FindClipTrack(c.Id)?.Id).Distinct().Count() != 1)
                return false;   // only clips from one track move across together
            var moving = clips.Select(c => c.Id).ToHashSet();
            double StartOf(Clip c) => starts.TryGetValue(c.Id, out var s) ? Math.Max(0, s) : c.StartSec;
            bool Free(Track track, Clip clip)
                => !track.Clips.Any(o => !moving.Contains(o.Id)
                    && o.StartSec < StartOf(clip) + clip.DurSec - 1e-9 && StartOf(clip) < o.StartSec + o.DurSec - 1e-9);

            if (movers.Any(c => !Free(target, c)))
                return false;
            foreach (var other in clips.Except(movers))
                if (FindClipTrack(other.Id) is { } own && !Free(own, other))
                    return false;

            foreach (var clip in clips)
                clip.StartSec = StartOf(clip);
            foreach (var clip in movers)
                if (FindClipTrack(clip.Id) is { } from && from.Id != target.Id)
                {
                    from.Clips.Remove(clip);
                    InsertClip(target, clip);
                }
            RefreshTrackIndices();
            return true;
        }

        public bool MoveClipToTrack(string clipId, string targetTrackId)
        {
            var clip = FindClip(clipId);
            if (clip is null)
                return false;
            var target = FindTrack(targetTrackId);
            if (target is null)
                return false;

            // the primary clip must be placed on a track of matching kind
            var clipKind = clip.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Video;
            if (target.Kind != clipKind)
                return false;

            var group = LinkGroup(clipId);

            // compute each member's target track (same kind), skipping ones already there
            var destinations = new List<(Clip Member, Track Target)>();
            foreach (var member in group)
            {
                var memberKind = member.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Video;
                var current = FindClipTrack(member.Id);
                var targetForMember = member.Id == clip.Id ? target : FindFreeTrack(memberKind, member, current);

                if (targetForMember is null || (current is not null && current.Id == targetForMember.Id))
                    continue;

                destinations.Add((member, targetForMember));
            }

            // overlap check on every destination
            foreach (var (member, dest) in destinations)
            {
                if (WouldOverlap(dest.Id, member.StartSec, member.DurSec, member.Id))
                    return false;
            }

            foreach (var (member, dest) in destinations)
            {
                var current = FindClipTrack(member.Id);
                current?.Clips.Remove(member);
                InsertClip(dest, member);
            }

            RaiseChanged();
            return true;
        }

        /// <summary>Finds a track of <paramref name="kind"/> where <paramref name="clip"/> fits without overlap, else null.</summary>
        private Track? FindFreeTrack(TrackKind kind, Clip clip, Track? current)
        {
            foreach (var track in Document.Tracks)
            {
                if (track.Kind != kind)
                    continue;
                if (current is not null && track.Id == current.Id)
                    continue;
                if (WouldOverlap(track.Id, clip.StartSec, clip.DurSec, clip.Id))
                    continue;
                return track;
            }
            // no free track of this kind -> create one
            return AddTrack(kind);
        }

        /// <summary>True when placing a clip of <paramref name="durSec"/> at <paramref name="startSec"/> on a track would overlap another clip.</summary>
        public bool WouldOverlap(string trackId, double startSec, double durSec, string? excludeClipId = null)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;
            var end = startSec + durSec;
            foreach (var clip in track.Clips)
            {
                if (excludeClipId is not null && clip.Id == excludeClipId)
                    continue;
                if (clip.StartSec < end && clip.StartSec + clip.DurSec > startSec)
                    return true;
            }
            return false;
        }

        /// <summary>Returns the id of the track holding <paramref name="clipId"/>, or null.</summary>
        public string? FindClipTrackId(string clipId)
        {
            return FindClipTrack(clipId)?.Id;
        }

        /// <summary>
        /// True when moving a link group to <paramref name="startSec"/> on <paramref name="trackId"/>
        /// would overlap a clip outside the group. Assumes all group members share the same start.
        /// </summary>
        public bool WouldOverlapGroup(string trackId, IReadOnlyCollection<string> groupIds, double startSec, double durSec)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;
            var end = startSec + durSec;
            foreach (var clip in track.Clips)
            {
                if (groupIds.Contains(clip.Id))
                    continue;
                if (clip.StartSec < end && clip.StartSec + clip.DurSec > startSec)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when moving the given clips to the given start times would make any of them overlap a clip
        /// that is not moving with them (on the track each currently sits on).
        /// </summary>
        public bool WouldOverlapMove(IReadOnlyDictionary<string, double> newStarts)
        {
            foreach (var (id, start) in newStarts)
            {
                var clip = FindClip(id);
                var track = FindClipTrack(id);
                if (clip is null || track is null)
                    continue;
                var end = start + clip.DurSec;
                foreach (var other in track.Clips)
                {
                    if (newStarts.ContainsKey(other.Id))
                        continue;
                    if (other.StartSec < end - 1e-9 && other.StartSec + other.DurSec > start + 1e-9)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Records a drag that moved or trimmed clips live as one undo step. <paramref name="before"/> is how the
        /// clips looked when the drag began; their current state is the result. Does nothing if nothing changed.
        /// </summary>
        public void CommitClipGeometry(IReadOnlyList<ClipGeometry> before)
        {
            var after = before.Select(b => FindClip(b.ClipId)).Where(c => c is not null)
                .Select(c => CaptureGeometry(c!)).ToList();
            var changed = false;
            for (var i = 0; i < before.Count && i < after.Count; i++)
                changed |= before[i] != after[i];
            if (!changed)
                return;
            History.Execute(new SetClipGeometryCommand(this, before, after));
            RaiseChanged();
        }

        /// <summary>Unlinks the given clips and everything linked to them (they then move and cut separately).</summary>
        public void UnlinkClips(IEnumerable<string> clipIds)
        {
            var changes = new Dictionary<string, string?>();
            foreach (var id in clipIds)
                foreach (var member in LinkGroup(id))
                    changes[member.Id] = null;
            if (changes.Count == 0)
                return;
            History.Execute(new SetLinkGroupsCommand(this, changes));
            RaiseChanged();
        }

        /// <summary>Links two or more clips so they move, trim and cut together.</summary>
        public void LinkClips(IEnumerable<string> clipIds)
        {
            var members = clipIds.SelectMany(id => LinkGroup(id)).GroupBy(c => c.Id).Select(g => g.First()).ToList();
            if (members.Count < 2)
                return;
            var group = Guid.NewGuid().ToString("N");
            History.Execute(new SetLinkGroupsCommand(this, members.ToDictionary(c => c.Id, _ => (string?)group)));
            RaiseChanged();
        }

        public void RippleInsert(string trackId, Clip clip, double posSec)
        {
            History.Execute(new RippleInsertCommand(this, trackId, clip, posSec));
            RaiseChanged();
        }

        public void OverwriteInsert(string trackId, Clip clip, double posSec)
        {
            History.Execute(new OverwriteInsertCommand(this, trackId, clip, posSec));
            RaiseChanged();
        }

        /// <summary>
        /// Splits selected clips (and their link-group affiliates) at the playhead.
        /// Unselected clips that merely overlap the same time are left alone.
        /// No-op when nothing is selected or no selected clip spans the playhead.
        /// </summary>
        public IReadOnlyList<Clip> SplitAtPlayhead(double posSec)
        {
            var snapped = SnapTime(posSec);
            var seeds = SelectedGroupSeeds();
            if (seeds.Count == 0)
                return Array.Empty<Clip>();

            return SplitGroupsAt(seeds, snapped);
        }

        /// <summary>
        /// Splits the clip under the playhead on <paramref name="trackId"/> (plus its link
        /// group). Never walks every track looking for coincidental overlaps — use this when
        /// the user has focused a track but nothing is selected yet. If a selection already
        /// exists, it wins and <paramref name="trackId"/> is ignored.
        /// </summary>
        public IReadOnlyList<Clip> SplitAtPlayhead(string trackId, double posSec)
        {
            var snapped = SnapTime(posSec);

            if (Selection.Count == 0)
            {
                var hit = FindClipAt(trackId, snapped);
                if (hit is null)
                    return Array.Empty<Clip>();
                Selection.SelectOnly(hit.Id);
                foreach (var member in LinkGroup(hit.Id))
                    Selection.Select(member.Id);
            }

            return SplitAtPlayhead(snapped);
        }

        private IReadOnlyList<Clip> SplitGroupsAt(IReadOnlyList<string> seeds, double snapped)
        {
            var targets = new List<(Clip Clip, string? SecondGroupId)>();
            var seenGroups = new HashSet<string>();

            foreach (var seedId in seeds)
            {
                var group = LinkGroup(seedId);
                var spanning = group
                    .Where(c => c.StartSec < snapped && snapped < c.StartSec + c.DurSec)
                    .ToList();
                if (spanning.Count == 0)
                    continue;

                var groupKey = string.IsNullOrEmpty(spanning[0].LinkGroupId)
                    ? spanning[0].Id
                    : spanning[0].LinkGroupId!;
                if (!seenGroups.Add(groupKey))
                    continue;

                var secondGroup = string.IsNullOrEmpty(spanning[0].LinkGroupId)
                    ? null
                    : Guid.NewGuid().ToString("N");
                foreach (var member in spanning)
                    targets.Add((member, secondGroup));
            }

            if (targets.Count == 0)
                return Array.Empty<Clip>();

            var commands = targets
                .Select(t => (IEditCommand)new CutCommand(this, t.Clip.Id, snapped, t.SecondGroupId))
                .ToArray();
            History.Execute(commands.Length == 1 ? commands[0] : new CompositeCommand(commands));

            var cutCommands = commands.Cast<CutCommand>().ToList();
            var produced = cutCommands.SelectMany(c => c.ProducedClips).ToList();

            // select right halves (+ link groups) so successive beat splits don't require re-clicking
            var rightIds = new HashSet<string>();
            foreach (var cut in cutCommands)
            {
                if (cut.ProducedClips.Count < 2)
                    continue;
                var right = cut.ProducedClips[1];
                foreach (var member in LinkGroup(right.Id))
                    rightIds.Add(member.Id);
            }
            if (rightIds.Count > 0)
                Selection.SelectClips(rightIds);

            RaiseChanged();
            return produced;
        }

        public void Lift(string clipId)
        {
            History.Execute(new LiftCommand(this, clipId));
            RaiseChanged();
        }

        public double SnapTime(double sec)
        {
            return FrameMath.SnapToFrame(sec, Document.Rate);
        }

        /// <summary>
        /// When enabled, drag/drop positions also snap to nearby clip boundaries
        /// (magnetic snapping) in addition to the frame grid.
        /// </summary>
        public bool MagneticSnap { get; set; }

        /// <summary>Default snap reach in seconds, used until the view sets a zoom-aware value.</summary>
        public const double DefaultSnapWindowSec = 0.25;

        /// <summary>
        /// How far (seconds) something may be from a snap target and still snap to it. The timeline sets this
        /// from the zoom so the reach stays the same number of pixels.
        /// </summary>
        public double SnapWindowSec { get; set; } = DefaultSnapWindowSec;

        /// <summary>
        /// Spacing (seconds) of the snap grid, or null for none. The grid is the timeline's visible gridlines;
        /// it is what you snap to when no clip edge, marker or playhead is closer.
        /// </summary>
        public double? SnapGridSec { get; set; }

        /// <summary>The playhead position, offered as a snap target (set by the application).</summary>
        public double? SnapPlayheadSec { get; set; }

        /// <summary>Snaps a time and returns what it snapped to. See <see cref="SnapMagnetic"/>.</summary>
        public double SnapTimeMagnetic(double sec) => SnapMagnetic(sec).Time;

        /// <summary>
        /// Snaps to the frame grid and nearby targets, ignoring the clip with <paramref name="excludeClipId"/>
        /// (and its link group) and the marker with <paramref name="excludeMarkerId"/>. Used when dragging so
        /// something snaps to *other* things, not itself.
        /// </summary>
        public double SnapTimeMagnetic(double sec, string? excludeClipId, string? excludeMarkerId = null)
            => SnapMagnetic(sec, excludeClipId, excludeMarkerId).Time;

        /// <summary>
        /// Snapping with a reason. When magnetic snapping is on, the closest of these within
        /// <see cref="SnapWindowSec"/> wins: clip edges, markers (and range edges), the playhead, the start of
        /// the timeline; failing those, the nearest grid line; failing that, the nearest frame.
        /// </summary>
        public SnapResult SnapMagnetic(double sec, string? excludeClipId = null, string? excludeMarkerId = null,
            bool includePlayhead = true, IReadOnlySet<string>? excludeClips = null)
        {
            var frame = SnapTime(sec);
            if (!MagneticSnap)
                return new SnapResult(frame, SnapKind.Frame);

            var excludeGroup = excludeClipId is null ? null : FindClip(excludeClipId)?.LinkGroupId;

            double? best = null;
            var bestKind = SnapKind.Frame;
            var bestDelta = double.MaxValue;

            foreach (var track in Document.Tracks)
                foreach (var clip in track.Clips)
                {
                    if (clip.Id == excludeClipId || (excludeGroup is not null && clip.LinkGroupId == excludeGroup)
                        || excludeClips?.Contains(clip.Id) == true)
                        continue;
                    Consider(clip.StartSec, SnapKind.ClipEdge);
                    Consider(clip.StartSec + clip.DurSec, SnapKind.ClipEdge);
                }

            foreach (var marker in Document.Markers)
            {
                if (marker.Id == excludeMarkerId)
                    continue;
                Consider(marker.StartSec, SnapKind.Marker);
                if (marker.IsRange)
                    Consider(marker.EndSec, SnapKind.Marker);
            }

            if (includePlayhead && SnapPlayheadSec is { } playhead)
                Consider(playhead, SnapKind.Playhead);
            // the start of the timeline is something to catch on, but only once there is something to line up with
            if (Document.Tracks.Any(t => t.Clips.Count > 0))
                Consider(0, SnapKind.ClipEdge);

            if (best is { } hit)
                return new SnapResult(hit, bestKind);

            if (SnapGridSec is { } grid && grid > 1e-6)
            {
                var line = Math.Round(sec / grid) * grid;
                if (Math.Abs(line - sec) <= SnapWindowSec)
                    return new SnapResult(SnapTime(line), SnapKind.Grid);
            }

            return new SnapResult(frame, SnapKind.Frame);

            void Consider(double target, SnapKind kind)
            {
                var delta = Math.Abs(target - sec);
                if (delta <= SnapWindowSec && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = target;
                    bestKind = kind;
                }
            }
        }

        public Clip? FindClipAt(string trackId, double posSec)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return null;
            foreach (var clip in track.Clips)
                if (posSec >= clip.StartSec && posSec < clip.StartSec + clip.DurSec)
                    return clip;
            return null;
        }

        public IReadOnlyList<Clip> ClipsOverlapping(string trackId, double startSec, double endSec)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return Array.Empty<Clip>();
            return track.Clips
                .Where(c => c.StartSec < endSec && c.StartSec + c.DurSec > startSec)
                .ToList();
        }

        public double TrackEnd(string trackId)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return 0;
            double end = 0;
            foreach (var clip in track.Clips)
                end = Math.Max(end, clip.StartSec + clip.DurSec);
            return end;
        }

        public void Execute(IEditCommand command)
        {
            History.Execute(command);
            RaiseChanged();
        }

        public bool Undo()
        {
            if (!History.Undo())
                return false;
            RaiseChanged();
            return true;
        }

        public bool Redo()
        {
            if (!History.Redo())
                return false;
            RaiseChanged();
            return true;
        }

        public void SetOpacity(string clipId, double opacity)
        {
            opacity = Math.Clamp(opacity, 0, 1);
            var clip = FindClip(clipId);
            if (clip is null || Math.Abs(clip.Opacity - opacity) < 1e-6)
                return;
            History.ExecuteCoalescing(new SetOpacityCommand(this, clipId, opacity));
            RaiseChanged();
        }

        /// <summary>Sets the fade-in of the clip's picture, or of a video clip's own sound (and its link group's).</summary>
        public void SetFadeIn(string clipId, double fadeInSec, FadeOf of = FadeOf.Picture)
        {
            fadeInSec = Math.Max(0, fadeInSec);
            var clip = FindClip(clipId);
            if (clip is null)
                return;
            var (current, fadeOut) = ClipFade.Get(clip, of);
            var clamped = Math.Min(fadeInSec, Math.Max(0, clip.DurSec - Math.Max(0, fadeOut)));
            if (Math.Abs(current - clamped) < 1e-6)
                return;
            History.ExecuteCoalescing(new SetFadeInCommand(this, clipId, fadeInSec, of));
            RaiseChanged();
        }

        /// <summary>Sets the fade-out of the clip's picture, or of a video clip's own sound (and its link group's).</summary>
        public void SetFadeOut(string clipId, double fadeOutSec, FadeOf of = FadeOf.Picture)
        {
            fadeOutSec = Math.Max(0, fadeOutSec);
            var clip = FindClip(clipId);
            if (clip is null)
                return;
            var (fadeIn, current) = ClipFade.Get(clip, of);
            var clamped = Math.Min(fadeOutSec, Math.Max(0, clip.DurSec - Math.Max(0, fadeIn)));
            if (Math.Abs(current - clamped) < 1e-6)
                return;
            History.ExecuteCoalescing(new SetFadeOutCommand(this, clipId, fadeOutSec, of));
            RaiseChanged();
        }

        public void AddEffect(string clipId, EffectInstance effect)
        {
            if (FindEffectOwner(clipId) is null)
                return;
            History.Execute(new AddEffectCommand(this, clipId, effect));
            RaiseChanged();
        }

        /// <summary>
        /// Limits an effect to part of the clip (or, with null start and length, puts it back on the whole
        /// clip). Moving a bar can carry its keyframes; resizing its edges leaves keyframe times unchanged.
        /// </summary>
        public void SetEffectRange(string clipId, string effectId, double? startSec, double? durSec, bool moveKeyframes = false)
        {
            var clip = FindEffectOwner(clipId);
            if (clip is null)
                return;
            if (startSec is { } s && durSec is { } d)
            {
                const double minLength = 0.1;
                d = Math.Clamp(d, Math.Min(minLength, clip.DurSec), EffectTimeLimit(clip));
                s = Math.Clamp(s, 0, Math.Max(0, EffectTimeLimit(clip) - d));
                startSec = s;
                durSec = d;
            }

            ExecuteEffectEdit(clipId, effectId, "Change effect range", $"range|{clipId}|{effectId}", fx =>
            {
                if (moveKeyframes && startSec is { } newStart)
                {
                    var delta = newStart - (fx.StartSec ?? 0);
                    foreach (var key in fx.Keyframes.Keys.ToArray())
                        fx.Keyframes[key] = fx.Keyframes[key].Select(point => point with
                        {
                            TimeSec = Math.Clamp(point.TimeSec + delta, 0, EffectTimeLimit(clip)),
                        }).ToList();
                }
                fx.StartSec = startSec;
                fx.DurSec = durSec;
            });
        }

        private void ExecuteEffectEdit(string clipId, string effectId, string description, string? coalesceKey,
            Action<EffectInstance> mutate)
        {
            if (FindEffectOwner(clipId)?.Effects.Any(e => e.Id == effectId) != true)
                return;
            var command = new EffectEditCommand(this, clipId, effectId, description, coalesceKey, mutate);
            if (coalesceKey is null)
                History.Execute(command);
            else
                History.ExecuteCoalescing(command);
            RaiseChanged();
        }

        /// <summary>
        /// Sets (or adds) the keyframe of an effect at a clip-relative time for every given parameter at once.
        /// Dragging a value coalesces into one undo step.
        /// </summary>
        public void SetEffectKeyframe(string clipId, string effectId, double timeSec,
            IReadOnlyDictionary<string, double> values, KeyframeEasing? easing = null)
            => EditKeyframes(KeyframeHostRef.OfEffect(clipId, effectId), "Set keyframe", $"kf|{clipId}|{effectId}|{Math.Round(timeSec, 4)}",
                host => Upsert(host, timeSec, values.ToDictionary(kv => kv.Key, kv => host.ToValue(kv.Key, kv.Value)), easing, host.NewKeyEasing));

        /// <summary>
        /// Adds a keyframe at a clip-relative time holding the effect's current look there, so adding it
        /// changes nothing visible. Keyframes hold their first and last values outside their range, so a
        /// single keyframe is already a valid (constant) animation. Returns the time keyed.
        /// </summary>
        public double? AddEffectKeyframe(string clipId, string effectId, double localSec)
            => AddKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), localSec);

        /// <summary>
        /// Turns animation on or off for an effect. On: a keyframe at the given time holds the current look.
        /// Off: every keyframe is removed and the look at that time becomes the constant value. This is the
        /// explicit "clear animation" edit; the stopwatch records with <see cref="RecordEffectKeyframe"/> instead.
        /// </summary>
        public void SetEffectAnimated(string clipId, string effectId, bool animated, double localSec)
        {
            var clip = FindEffectOwner(clipId);
            if (clip is null)
                return;
            var time = Math.Clamp(localSec, 0, EffectTimeLimit(clip));
            ExecuteEffectEdit(clipId, effectId, animated ? "Animate effect" : "Stop animating effect", null, fx =>
            {
                if (animated)
                {
                    if (fx.Keyframes.Count == 0)
                        UpsertKeyframes(fx, time, CurrentNumericValues(fx, time), null);
                    return;
                }
                var frozen = CurrentNumericValues(fx, time);
                fx.Keyframes.Clear();
                var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema ?? Array.Empty<ParamDef>();
                foreach (var (key, number) in frozen)
                {
                    var kind = schema.FirstOrDefault(p => p.Key == key)?.Kind ?? ParamKind.Double;
                    fx.Params[key] = kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
                }
            });
        }

        /// <summary>
        /// Records a keyframe at a clip-relative time holding the effect's current look there (the stopwatch).
        /// Recording only ever adds: an effect that is not animated yet starts with this keyframe, one that is
        /// keeps every keyframe it has, and a time that already has a keyframe is left as it is. Returns the
        /// time keyed, or null when the effect does not exist. Removing keyframes is a separate, explicit edit
        /// (<see cref="RemoveEffectKeyframe"/>, <see cref="SetEffectAnimated"/>).
        /// </summary>
        public double? RecordEffectKeyframe(string clipId, string effectId, double localSec)
            => RecordKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), localSec);

        /// <summary>Copies the keyframe at one time to another time (same values and easing).</summary>
        public void DuplicateEffectKeyframe(string clipId, string effectId, double fromSec, double toSec)
            => DuplicateKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), fromSec), toSec);

        private static Dictionary<string, double> CurrentNumericValues(EffectInstance fx, double localT)
        {
            var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema ?? Array.Empty<ParamDef>();
            var resolved = Fig.Core.Media.EffectPipeline.ResolveParams(fx, localT);
            var values = new Dictionary<string, double>();
            foreach (var def in schema)
                if (def.Kind is ParamKind.Double or ParamKind.Int && resolved.TryGetValue(def.Key, out var v))
                    values[def.Key] = v.AsNumber;
            return values;
        }

        private static void UpsertKeyframes(EffectInstance fx, double timeSec,
            IReadOnlyDictionary<string, double> values, KeyframeEasing? easing)
        {
            var schema = Fig.Core.Media.EffectCatalog.Find(fx.TypeId)?.ParamSchema;
            foreach (var (key, number) in values)
            {
                var kind = schema?.FirstOrDefault(p => p.Key == key)?.Kind ?? ParamKind.Double;
                if (kind is not (ParamKind.Double or ParamKind.Int))
                    continue;
                var value = kind == ParamKind.Int ? ParamValue.OfInt((int)Math.Round(number)) : ParamValue.OfDouble(number);
                if (!fx.Keyframes.TryGetValue(key, out var track))
                    fx.Keyframes[key] = track = new List<KeyframePoint>();
                var i = track.FindIndex(p => Math.Abs(p.TimeSec - timeSec) < 1e-6);
                if (i >= 0)
                    track[i] = new KeyframePoint(timeSec, value, easing ?? track[i].Easing);
                else
                {
                    track.Add(new KeyframePoint(timeSec, value, easing ?? KeyframeEasing.EaseInOut));
                    track.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
                }
            }
        }

        /// <summary>Removes the keyframe at a clip-relative time from every parameter of an effect.</summary>
        public void RemoveEffectKeyframe(string clipId, string effectId, double timeSec)
            => RemoveKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), timeSec));

        /// <summary>
        /// Moves a keyframe to a new time on every parameter of an effect. Pass the same
        /// <paramref name="gesture"/> for every step of one drag so it undoes in one go.
        /// </summary>
        public void MoveEffectKeyframe(string clipId, string effectId, double fromSec, double toSec, string gesture)
            => MoveKeyframes(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), fromSec), toSec, gesture);

        /// <summary>Changes how the effect eases from the keyframe at a time to the next one.</summary>
        public void SetEffectKeyframeEasing(string clipId, string effectId, double timeSec, KeyframeEasing easing)
            => SetKeyframeEasing(new KeyframeRef(KeyframeHostRef.OfEffect(clipId, effectId), timeSec), easing);

        public void RemoveEffect(string clipId, string effectId)
        {
            if (FindEffectOwner(clipId) is null)
                return;
            History.Execute(new RemoveEffectCommand(this, clipId, effectId));
            RaiseChanged();
        }

        /// <summary>Sets a typed parameter on an effect (slider drags coalesce into one undo step).</summary>
        public void SetEffectParam(string clipId, string effectId, string key, ParamValue value)
        {
            var clip = FindEffectOwner(clipId);
            if (clip is null)
                return;
            EffectInstance? effect = null;
            foreach (var e in clip.Effects)
                if (e.Id == effectId)
                {
                    effect = e;
                    break;
                }
            if (effect is null || !effect.Params.TryGetValue(key, out var old) || old == value)
                return;
            History.ExecuteCoalescing(new SetEffectParamCommand(this, clipId, effectId, key, value));
            RaiseChanged();
        }

        public void ToggleEffect(string clipId, string effectId)
        {
            if (FindEffectOwner(clipId) is null)
                return;
            History.Execute(new ToggleEffectCommand(this, clipId, effectId));
            RaiseChanged();
        }

        /// <summary>Sets a typed parameter on the transition across a cut (writes both clip edges).</summary>
        public void SetTransitionParam(string leftClipId, string rightClipId, string key, ParamValue value)
        {
            var left = FindClip(leftClipId);
            var right = FindClip(rightClipId);
            if (left is null || right is null)
                return;
            TransitionRef? any = left.TransitionOut ?? right.TransitionIn;
            if (any is null)
                return;
            var old = any.Params.TryGetValue(key, out var v) ? v : default(ParamValue);
            if (old == value)
                return;
            History.ExecuteCoalescing(new SetTransitionParamCommand(this, leftClipId, rightClipId, key, value));
            RaiseChanged();
        }

        // ---- effect keyframes ----

        public void SetKeyframe(string clipId, string effectId, string key, double timeSec, ParamValue value)
            => SetKeyframeValue(KeyframeHostRef.OfEffect(clipId, effectId), key, timeSec, _ => value, null);

        public void RemoveKeyframe(string clipId, string effectId, string key, double timeSec)
            => RemoveKeyframe(KeyframeHostRef.OfEffect(clipId, effectId), key, timeSec);

        public void ClearKeyframes(string clipId, string effectId, string key)
            => ClearKeyframes(KeyframeHostRef.OfEffect(clipId, effectId), key);

        // ---- clip automation keyframes ----

        public void SetClipKeyframe(string clipId, string key, double timeSec, double value)
            => SetKeyframeValue(ClipHost(clipId, key), key, timeSec, value);

        public void RemoveClipKeyframe(string clipId, string key, double timeSec)
            => RemoveKeyframe(ClipHost(clipId, key), key, timeSec);

        public void ClearClipKeyframes(string clipId, string? key)
            => ClearKeyframes(key is null ? KeyframeHostRef.OfClip(clipId) : ClipHost(clipId, key), key);

        /// <summary>Which of a clip's keyframe hosts a property belongs to: its speed curve, or its own keys.</summary>
        private static KeyframeHostRef ClipHost(string clipId, string key)
            => key == AutomationKeys.Speed ? KeyframeHostRef.OfSpeed(clipId) : KeyframeHostRef.OfClip(clipId);

        public void SetTransitionIn(string clipId, TransitionRef? transition)
        {
            if (FindClip(clipId) is null)
                return;
            History.Execute(new SetTransitionInCommand(this, clipId, transition));
            RaiseChanged();
        }

        public void SetTransitionOut(string clipId, TransitionRef? transition)
        {
            // transitions are a video-only concept; the transition resolver only scans
            // video tracks, so an edge on an audio clip would silently never render
            if (FindClip(clipId) is not VideoClip)
                return;
            History.Execute(new SetTransitionOutCommand(this, clipId, transition));
            RaiseChanged();
        }

        /// <summary>
        /// Applies a catalog transition to the cut between two abutting clips
        /// (outgoing.transitionOut + incoming.transitionIn). Video clips only.
        /// </summary>
        public bool ApplyTransitionAtCut(string outgoingClipId, string incomingClipId, TransitionRef transition)
        {
            if (FindClip(outgoingClipId) is not VideoClip left || FindClip(incomingClipId) is not VideoClip right)
                return false;
            var requestedDuration = transition.DurationSec;
            var duration = ClampTransitionDuration(left, right, requestedDuration);
            if (duration < 2.0 / Math.Max(1, Document.Rate.Fps))
            {
                TransitionValidationMessage = "The neighboring clips are too short for a transition.";
                return false;
            }
            transition = transition.Clone();
            transition.DurationSec = duration;
            History.Execute(new ApplyTransitionAtCutCommand(this, outgoingClipId, incomingClipId, transition));
            RaiseChanged();
            TransitionValidationMessage = duration + 1e-6 < requestedDuration
                ? "Transition duration was limited by neighboring clip length."
                : null;
            return true;
        }

        /// <summary>
        /// Applies <paramref name="transition"/> as transition-out on the selected clip and
        /// transition-in on the next abutting clip on the same track, when one exists.
        /// Otherwise sets transition-out only. Returns false when the clips are not video
        /// (transitions only exist between video clips).
        /// </summary>
        public bool TryApplyTransitionFromSelection(string clipId, TransitionRef transition)
        {
            var clip = FindClip(clipId);
            var track = FindClipTrack(clipId);
            if (clip is not VideoClip || track is null)
                return false;

            var cut = clip.StartSec + clip.DurSec;
            Clip? next = null;
            foreach (var other in track.Clips)
            {
                if (other.Id == clip.Id)
                    continue;
                if (Math.Abs(other.StartSec - cut) < 1e-3)
                {
                    next = other;
                    break;
                }
            }

            if (next is not null)
            {
                if (next is not VideoClip)
                    return false;
                return ApplyTransitionAtCut(clip.Id, next.Id, transition);
            }
            TransitionValidationMessage = "Transitions require an abutting video clip at the cut.";
            return false;
        }

        /// <summary>Enumerates the transitions living across abutting cuts on a track.</summary>
        public IEnumerable<CutTransition> EnumerateTransitions(Track track)
        {
            var sorted = track.Clips.OrderBy(c => c.StartSec).ToList();
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var left = sorted[i];
                var right = sorted[i + 1];
                if (Math.Abs(right.StartSec - (left.StartSec + left.DurSec)) > 1e-3)
                    continue;
                var transition = ResolveCutTransition(left, right);
                if (transition is not null)
                    yield return transition;
            }
        }

        public CutTransition? GetTransition(string key)
        {
            var parts = key.Split('|', 2);
            if (parts.Length != 2)
                return null;
            var left = FindClip(parts[0]);
            var right = FindClip(parts[1]);
            if (left is null || right is null)
                return null;
            return ResolveCutTransition(left, right);
        }

        /// <summary>Removes the transition at a cut (undoable). Clears the selection when it points there.</summary>
        public void RemoveTransition(string leftClipId, string rightClipId)
        {
            var left = FindClip(leftClipId);
            var right = FindClip(rightClipId);
            if (left is null || right is null || (left.TransitionOut is null && right.TransitionIn is null))
                return;
            History.Execute(new RemoveTransitionCommand(this, leftClipId, rightClipId));
            if (Selection.SelectedTransitionKey == $"{leftClipId}|{rightClipId}")
                Selection.SelectedTransitionKey = null;
            RaiseChanged();
        }

        public void RemoveSelectedTransition()
        {
            if (Selection.SelectedTransitionKey is not { } key)
                return;
            var transition = GetTransition(key);
            if (transition is null)
            {
                Selection.SelectedTransitionKey = null;
                return;
            }
            RemoveTransition(transition.LeftClipId, transition.RightClipId);
        }

        /// <summary>Resizes a cut transition (drag/slider updates coalesce into one undo step).</summary>
        public void SetTransitionDuration(string leftClipId, string rightClipId, double durationSec)
        {
            var left = FindClip(leftClipId);
            var right = FindClip(rightClipId);
            if (left is null || right is null || (left.TransitionOut is null && right.TransitionIn is null))
                return;
            if (left is VideoClip leftVideo && right is VideoClip rightVideo)
                durationSec = ClampTransitionDuration(leftVideo, rightVideo, durationSec);
            if (durationSec <= 1e-6)
                return;
            History.ExecuteCoalescing(new SetTransitionDurationCommand(this, leftClipId, rightClipId, durationSec));
            RaiseChanged();
        }

        public double GetMaxTransitionDuration(string leftClipId, string rightClipId)
        {
            if (FindClip(leftClipId) is not VideoClip left || FindClip(rightClipId) is not VideoClip right)
                return 0;
            return ClampTransitionDuration(left, right, double.MaxValue);
        }

        private double ClampTransitionDuration(VideoClip left, VideoClip right, double requested)
        {
            var visibleLimit = 2 * Math.Min(left.DurSec, right.DurSec);
            // Transitions remain usable on fresh, untrimmed clips. Rendering samples real
            // source handles when present and repeats the nearest edge frame when absent.
            return Math.Clamp(requested, 0, visibleLimit);
        }

        /// <summary>Resizes a cut transition identified by its selection key ("{left}|{right}").</summary>
        public void SetTransitionDuration(string key, double durationSec)
        {
            var transition = GetTransition(key);
            if (transition is not null)
                SetTransitionDuration(transition.LeftClipId, transition.RightClipId, durationSec);
        }

        private static CutTransition? ResolveCutTransition(Clip left, Clip right)
        {
            if (left.TransitionOut is null && right.TransitionIn is null)
                return null;
            var typeId = left.TransitionOut?.TypeId ?? right.TransitionIn!.TypeId;
            var dur = Math.Max(left.TransitionOut?.DurationSec ?? 0, right.TransitionIn?.DurationSec ?? 0);
            return new CutTransition(left.Id, right.Id, left, right, typeId, dur, left.StartSec + left.DurSec);
        }

        public void SetVolume(string clipId, double volume)
        {
            volume = Math.Clamp(volume, 0, 1);
            var clip = FindClip(clipId);
            if (clip is null || Math.Abs(clip.Volume - volume) < 1e-6)
                return;
            History.ExecuteCoalescing(new SetVolumeCommand(this, clipId, volume));
            RaiseChanged();
        }

        public void SetSpeed(string clipId, double speed)
        {
            speed = Math.Clamp(speed, 0.1, 8.0);
            var clip = FindClip(clipId);
            // a clip with a speed curve takes its speed from the curve
            if (clip is null || Math.Abs(clip.Speed - speed) < 1e-6 || ClipTime.HasCurve(clip))
                return;
            History.ExecuteCoalescing(new SetSpeedCommand(this, clipId, speed));
            RaiseChanged();
        }

        public void SetCrop(string clipId, double cropL, double cropT, double cropR, double cropB)
        {
            if (FindClip(clipId) is not ICroppable clip)
                return;
            (cropL, cropT, cropR, cropB) = SetCropCommand.Normalize(cropL, cropT, cropR, cropB);
            if (Near(clip.CropL, cropL) && Near(clip.CropT, cropT)
                && Near(clip.CropR, cropR) && Near(clip.CropB, cropB))
                return;
            History.ExecuteCoalescing(new SetCropCommand(this, clipId, cropL, cropT, cropR, cropB));
            RaiseChanged();
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

        protected void RaiseChanged()
        {
            // A preview for a clip that was deleted (or undone away) has nothing to attach to.
            if (BeatPreview is { } preview && !Document.Tracks.Any(track => track.Clips.Contains(preview.Clip)))
                ClearBeatPreview();
            TimelineChanged?.Invoke();
        }

        /// <summary>Notifies the view that media metadata changed (e.g. a filmstrip finished backfilling).</summary>
        public void NotifyMediaChanged()
        {
            RaiseChanged();
        }

        /// <summary>The clip with this id, or null.</summary>
        public IEffectOwner? FindEffectOwner(string ownerId)
            => (IEffectOwner?)Document.Tracks.FirstOrDefault(t => t.Id == ownerId) ?? FindClip(ownerId);

        private static double EffectTimeLimit(IEffectOwner owner)
            => owner is Track ? double.PositiveInfinity : owner.DurSec;

        public Clip? FindClipForView(string clipId) => FindClip(clipId);

        internal Clip? FindClip(string clipId)
        {
            foreach (var track in Document.Tracks)
            {
                var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
                if (clip is not null)
                    return clip;
            }
            return null;
        }

        internal Track? FindTrack(string trackId)
        {
            foreach (var track in Document.Tracks)
                if (track.Id == trackId)
                    return track;
            return null;
        }

        internal Track? FindClipTrack(string clipId)
        {
            foreach (var track in Document.Tracks)
                foreach (var clip in track.Clips)
                    if (clip.Id == clipId)
                        return track;
            return null;
        }

        internal void InsertClip(Track track, Clip clip)
        {
            var index = 0;
            while (index < track.Clips.Count && track.Clips[index].StartSec < clip.StartSec)
                index++;
            track.Clips.Insert(index, clip);
        }

        internal void RemoveClip(Track track, Clip clip)
        {
            track.Clips.Remove(clip);
        }

        public Track AddTrack(TrackKind kind)
        {
            var track = new Track
            {
                Kind = kind,
                Name = kind == TrackKind.Video ? $"V{CountTracks(TrackKind.Video) + 1}" : $"A{CountTracks(TrackKind.Audio) + 1}",
            };
            var before = Document.Tracks.ToList();
            History.Execute(new SetTrackListCommand(this, "Add track", before, WithNewTrack(before, track)));
            RaiseChanged();
            return track;
        }

        /// <summary>
        /// The track list with <paramref name="track"/> added: a video track on top of the picture (above the other
        /// video tracks, see <see cref="TrackStacking"/>), an audio track at the end.
        /// </summary>
        private static List<Track> WithNewTrack(IReadOnlyList<Track> tracks, Track track)
        {
            var after = tracks.ToList();
            var topVideo = after.FindIndex(t => t.Kind == TrackKind.Video);
            if (track.Kind == TrackKind.Video && topVideo >= 0)
                after.Insert(topVideo, track);
            else
                after.Add(track);
            return after;
        }

        /// <summary>
        /// Moves a track so it sits before the track now at <paramref name="insertIndex"/> (0 is the top; the track
        /// count is the end). Higher video tracks cover lower ones, so this changes the picture's stacking. One undo step.
        /// </summary>
        public bool MoveTrack(string trackId, int insertIndex)
        {
            var before = Document.Tracks.ToList();
            var from = before.FindIndex(t => t.Id == trackId);
            if (from < 0)
                return false;
            insertIndex = Math.Clamp(insertIndex, 0, before.Count);
            var to = insertIndex > from ? insertIndex - 1 : insertIndex;
            if (to == from)
                return false;
            var after = before.ToList();
            var track = after[from];
            after.RemoveAt(from);
            after.Insert(to, track);
            History.Execute(new SetTrackListCommand(this, "Move track", before, after));
            RaiseChanged();
            return true;
        }

        public bool RemoveTrack(string trackId)
        {
            var track = FindTrack(trackId);
            if (track is null)
                return false;

            var before = Document.Tracks.ToList();
            var after = before.Where(t => t.Id != trackId).ToList();
            History.Execute(new SetTrackListCommand(this, "Delete track", before, after));
            Selection.Clear();
            RaiseChanged();
            return true;
        }

        /// <summary>Shows or hides a video track (undoable; the preview and autosave follow).</summary>
        public void SetTrackVisible(string trackId, bool visible)
        {
            if (FindTrack(trackId) is not { } track || track.Visible == visible)
                return;
            History.Execute(new SetTrackFlagCommand(track, muted: false, value: visible));
            RaiseChanged();
        }

        /// <summary>Mutes or unmutes an audio track (undoable).</summary>
        public void SetTrackMuted(string trackId, bool muted)
        {
            if (FindTrack(trackId) is not { } track || track.Muted == muted)
                return;
            History.Execute(new SetTrackFlagCommand(track, muted: true, value: muted));
            RaiseChanged();
        }

        /// <summary>Renames a track (an empty name goes back to the automatic V1 / A1 style label).</summary>
        public void RenameTrack(string trackId, string? name)
        {
            if (FindTrack(trackId) is not { } track)
                return;
            History.Execute(new RenameTrackCommand(track, name));
            RaiseChanged();
        }

        private int CountTracks(TrackKind kind)
        {
            var count = 0;
            foreach (var track in Document.Tracks)
                if (track.Kind == kind)
                    count++;
            return count;
        }

        private void ReindexTracks()
        {
            var v = 0;
            var a = 0;
            for (var i = 0; i < Document.Tracks.Count; i++)
            {
                var track = Document.Tracks[i];
                track.Index = i;
                if (track.Kind == TrackKind.Video)
                    track.Name = $"V{++v}";
                else
                    track.Name = $"A{++a}";
            }
        }

        /// <summary>
        /// Re-syncs each track's <see cref="Track.Index"/> (and name) with its position in the
        /// list. Called on project load because Index is serialized and can be stale.
        /// </summary>
        public void RefreshTrackIndices()
        {
            ReindexTracks();
        }

        public Track EnsureTrack(TrackKind kind)
        {
            foreach (var track in Document.Tracks)
                if (track.Kind == kind)
                    return track;
            return AddTrack(kind);
        }

        public Track FindTrackForClip(Clip clip)
        {
            return FindClipTrack(clip.Id) ?? EnsureTrack(clip.Kind switch
            {
                ClipKind.Audio => TrackKind.Audio,
                _ => TrackKind.Video,
            });
        }

        // ---- markers ----

        public IEnumerable<MarkerLocation> AllMarkers()
        {
            foreach (var marker in Document.Markers)
                yield return new MarkerLocation(marker, null, null, Document);
            foreach (var track in Document.Tracks)
            {
                foreach (var marker in track.Markers)
                    yield return new MarkerLocation(marker, null, track, Document);
                foreach (var clip in track.Clips)
                    foreach (var marker in clip.Markers)
                        yield return new MarkerLocation(marker, clip, track, Document);
            }
        }

        public void SelectMarker(string id, bool toggle = false, bool extend = false, IReadOnlyCollection<string>? visibleIds = null)
        {
            if (FindMarker(id) is null)
                return;
            if (extend)
            {
                var ordered = AllMarkers().Where(m => visibleIds is null || visibleIds.Contains(m.Marker.Id))
                    .OrderBy(m => (m.Clip?.StartSec ?? 0) + m.Marker.StartSec)
                    .ThenBy(m => m.Marker.Id).Select(m => m.Marker.Id).ToArray();
                Selection.SelectMarkerRange(id, ordered, toggle);
            }
            else if (toggle)
                Selection.ToggleMarker(id);
            else
                Selection.SelectMarker(id);
        }

        /// <summary>Resolves where a marker lives. Returns null when it no longer exists.</summary>
        public MarkerLocation? FindMarker(string markerId)
        {
            foreach (var m in Document.Markers)
                if (m.Id == markerId)
                    return new MarkerLocation(m, null, null, Document);

            foreach (var track in Document.Tracks)
            {
                foreach (var m in track.Markers)
                    if (m.Id == markerId)
                        return new MarkerLocation(m, null, track, Document);
                foreach (var clip in track.Clips)
                    foreach (var m in clip.Markers)
                        if (m.Id == markerId)
                            return new MarkerLocation(m, clip, track, Document);
            }
            return null;
        }

        /// <summary>Adds a marker on a clip at a local offset, clamped into the clip's range.</summary>
        public Marker AddMarker(Clip clip, double localSec, string name = "", string color = "#ffd60a")
        {
            localSec = Math.Clamp(localSec, 0, clip.DurSec);
            var cmd = new AddMarkerCommand(clip, null, Document, localSec, name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>Adds clip-local beat times as one undoable operation.</summary>
        public IReadOnlyList<Marker> AddBeatMarkers(Clip clip, IReadOnlyList<double> times)
        {
            var command = new GenerateMarkersFromBeats(this, clip, times);
            if (!command.HasChanges)
                return Array.Empty<Marker>();
            History.Execute(command);
            RaiseChanged();
            return command.Markers;
        }

        /// <summary>Adds a marker on a track at an absolute timeline time.</summary>
        public Marker AddMarker(Track track, double sec, string name = "", string color = "#ffd60a")
        {
            var cmd = new AddMarkerCommand(null, track, Document, Math.Max(0, sec), name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>Adds a marker on the timeline at an absolute time.</summary>
        public Marker AddMarker(Timeline timeline, double sec, string name = "", string color = "#ffd60a")
        {
            var cmd = new AddMarkerCommand(null, null, timeline, Math.Max(0, sec), name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        public void DeleteMarker(string markerId)
        {
            DeleteMarkers(new[] { markerId });
        }

        public int DeleteMarkers(IEnumerable<string> markerIds)
        {
            var ids = markerIds.Distinct().Where(id => FindMarker(id) is not null).ToArray();
            if (ids.Length == 0)
                return 0;
            History.Execute(new CompositeCommand(ids.Select(id => (IEditCommand)new DeleteMarkerCommand(this, id)).ToArray()));
            if (Selection.SelectedMarkerIds.Any(ids.Contains))
                Selection.SelectMarkers(Selection.SelectedMarkerIds.Except(ids).ToArray());
            RaiseChanged();
            return ids.Length;
        }

        public int ClearClipMarkers(Clip clip, bool generatedOnly = false)
        {
            return DeleteMarkers(clip.Markers.Where(marker => !generatedOnly
                || marker.IsGeneratedBeat).Select(marker => marker.Id).ToArray());
        }

        /// <summary>Adds a range marker to the timeline (one undo step).</summary>
        public Marker AddRangeMarker(double startSec, double durSec, string name = "", string color = "#5f9cf8")
        {
            var cmd = new AddRangeMarkerCommand(Document, startSec, durSec, name, color);
            History.Execute(cmd);
            RaiseChanged();
            return cmd.Marker;
        }

        /// <summary>
        /// Creates a range from a timeline bookmark: with <paramref name="asStart"/> it runs from the
        /// bookmark to the next bookmark (or the end of the sequence); otherwise from the previous
        /// bookmark (or the start) to this one. Returns null when the bookmark is not a timeline
        /// point marker or there is no room for a range.
        /// </summary>
        public Marker? AddRangeFromBookmark(string markerId, bool asStart)
        {
            var bookmark = Document.Markers.FirstOrDefault(m => m.Id == markerId && !m.IsRange);
            if (bookmark is null)
                return null;
            var others = Document.Markers.Where(m => !m.IsRange && m.Id != bookmark.Id).ToList();
            string fromName, toName;
            double from, to;
            if (asStart)
            {
                var next = others.Where(m => m.StartSec > bookmark.StartSec + 1e-3).OrderBy(m => m.StartSec).FirstOrDefault();
                from = bookmark.StartSec;
                to = next?.StartSec ?? BookmarkRanges.SequenceEndSec(Document);
                fromName = bookmark.Name;
                toName = next?.Name ?? "Sequence end";
            }
            else
            {
                var previous = others.Where(m => m.StartSec < bookmark.StartSec - 1e-3).OrderByDescending(m => m.StartSec).FirstOrDefault();
                from = previous?.StartSec ?? 0;
                to = bookmark.StartSec;
                fromName = previous?.Name ?? "Sequence start";
                toName = bookmark.Name;
            }
            if (to - from < 1e-3)
                return null;
            var name = $"{(string.IsNullOrWhiteSpace(fromName) ? "Bookmark" : fromName)} \u2192 {(string.IsNullOrWhiteSpace(toName) ? "Bookmark" : toName)}";
            return AddRangeMarker(from, to - from, name, bookmark.Color);
        }

        /// <summary>Changes several marker fields at once (drags coalesce into one undo step).</summary>
        public void EditMarker(string markerId, MarkerEdit edit)
        {
            if (FindMarker(markerId) is null)
                return;
            History.ExecuteCoalescing(new EditMarkerCommand(this, markerId, edit));
            RaiseChanged();
        }

        /// <summary>
        /// Creates one range marker per section between bookmarks. Existing markers are kept.
        /// All new ranges undo together. Returns the markers created.
        /// </summary>
        public IReadOnlyList<Marker> CreateRangesFromBookmarks(bool includeSequenceBounds)
        {
            var plan = BookmarkRanges.Plan(Document, includeSequenceBounds);
            if (plan.Count == 0)
                return Array.Empty<Marker>();
            var commands = plan
                .Select(r => new AddRangeMarkerCommand(Document, r.StartSec, r.EndSec - r.StartSec, r.Name, r.Color))
                .ToList();
            History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands.ToArray<IEditCommand>()));
            RaiseChanged();
            return commands.Select(c => c.Marker).ToList();
        }

        public void MoveMarker(string markerId, double newSec)
        {
            var loc = FindMarker(markerId);
            if (loc is null)
                return;
            var clamped = loc.Clip is not null ? Math.Clamp(newSec, 0, loc.Clip.DurSec) : Math.Max(0, newSec);
            History.ExecuteCoalescing(new MoveMarkerCommand(this, markerId, clamped));
            RaiseChanged();
        }

        public void UpdateMarker(string markerId, string? name = null, string? color = null)
        {
            if (FindMarker(markerId) is null || (name is null && color is null))
                return;
            History.Execute(new UpdateMarkerCommand(this, markerId, name, color));
            RaiseChanged();
        }

        // ---- clip enable / disable ----

        public void ToggleEnabled(string clipId)
        {
            if (FindClip(clipId) is null)
                return;
            History.Execute(new ToggleEnabledCommand(this, new[] { clipId }));
            RaiseChanged();
        }

        /// <summary>Toggles Enabled on every distinct selected link group.</summary>
        public void ToggleEnabledSelected()
        {
            var seeds = SelectedGroupSeeds();
            if (seeds.Count == 0)
                return;
            History.Execute(new ToggleEnabledCommand(this, seeds));
            RaiseChanged();
        }
    }
}
