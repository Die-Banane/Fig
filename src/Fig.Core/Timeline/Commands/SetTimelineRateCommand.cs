namespace Fig.Core.Timeline
{
    /// <summary>Sets the project frame rate and marks it chosen, so importing media leaves it alone.</summary>
    public sealed class SetTimelineRateCommand : IEditCommand
    {
        private readonly TimelineEditor _editor;
        private readonly FrameRate _rate;
        private readonly FrameRate _oldRate;
        private readonly bool _oldChosen;

        public string Description => "set frame rate";

        public SetTimelineRateCommand(TimelineEditor editor, FrameRate rate)
        {
            _editor = editor;
            _rate = rate;
            _oldRate = editor.Document.Rate;
            _oldChosen = editor.Document.RateChosen;
        }

        public void Execute() => Apply(_rate, true);

        public void Undo() => Apply(_oldRate, _oldChosen);

        public void Redo() => Execute();

        private void Apply(FrameRate rate, bool chosen)
        {
            _editor.Document.Rate = rate;
            _editor.Document.RateChosen = chosen;
        }
    }

    /// <summary>Sets the picture's shape.</summary>
    public sealed class SetFrameShapeCommand(TimelineEditor editor, FrameShape shape) : IEditCommand
    {
        private readonly FrameShape _old = editor.Document.Shape;

        public string Description => "set frame shape";

        public void Execute() => editor.Document.Shape = shape;

        public void Undo() => editor.Document.Shape = _old;

        public void Redo() => Execute();
    }
}
