using System.Collections.Generic;
using System.Linq;

namespace Fig.Core.Timeline
{
    /// <summary>
    /// Adds, removes or reorders tracks by swapping the timeline's track list. Tracks keep their clips, so
    /// undoing a delete brings the clips back with it.
    /// </summary>
    public sealed class SetTrackListCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly IReadOnlyList<Track> _before;
        private readonly IReadOnlyList<Track> _after;

        public string Description { get; }

        public SetTrackListCommand(TimelineEditor editor, string description, IReadOnlyList<Track> before, IReadOnlyList<Track> after)
        {
            _editor = editor;
            Description = description;
            _before = before;
            _after = after;
        }

        public void Execute() => Apply(_after);

        public void Undo() => Apply(_before);

        public void Redo() => Apply(_after);

        private void Apply(IReadOnlyList<Track> tracks)
        {
            _editor.Document.Tracks.Clear();
            _editor.Document.Tracks.AddRange(tracks);
            _editor.RefreshTrackIndices();
        }
    }

    /// <summary>Renames a track.</summary>
    public sealed class RenameTrackCommand : IEditCommand
    {
        private readonly Track _track;
        private readonly string? _after;
        private string? _before;

        public string Description => "Rename track";

        public RenameTrackCommand(Track track, string? name)
        {
            _track = track;
            _after = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        public void Execute()
        {
            _before = _track.Name;
            _track.Name = _after;
        }

        public void Undo() => _track.Name = _before;

        public void Redo() => _track.Name = _after;
    }

    /// <summary>Shows/hides a video track or mutes/unmutes an audio track.</summary>
    public sealed class SetTrackFlagCommand : IEditCommand
    {
        private readonly Track _track;
        private readonly bool _muted;
        private readonly bool _value;
        private bool _before;

        public string Description => _muted ? "Mute track" : "Show or hide track";

        public SetTrackFlagCommand(Track track, bool muted, bool value)
        {
            _track = track;
            _muted = muted;
            _value = value;
        }

        public void Execute()
        {
            _before = _muted ? _track.Muted : _track.Visible;
            Set(_value);
        }

        public void Undo() => Set(_before);

        public void Redo() => Set(_value);

        private void Set(bool value)
        {
            if (_muted)
                _track.Muted = value;
            else
                _track.Visible = value;
        }
    }
}
