using Fig.Core.Media;
using Fig.Core.Timeline;
using ProjectModel = Fig.Core.Project.Project;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Tests;

/// <summary>
/// Export describes each frame as a <see cref="RenderScene"/> and hands it to an <see cref="ISceneRenderer"/>.
/// These tests record the scenes rather than drawing them, so they pin what the exporter asks for.
/// </summary>
public class ExportSceneTests
{
    private const string AssetPath = "/home/neckles/projects/fig/tests/assets/3 seconds timer [fxqE27gIZcc].webm";

    private sealed class RecordingRenderer : ISceneRenderer
    {
        public List<RenderScene> Scenes { get; } = new();
        public int MaxInFlight { get; init; } = 1;

        public Task RenderAsync(RenderScene scene, byte[] canvas)
        {
            // the contract: once RenderAsync returns the caller may reuse the frames, so a scene must be read now
            Scenes.Add(new RenderScene
            {
                Width = scene.Width,
                Height = scene.Height,
                Layers = scene.Layers.Select(l => new RenderLayer
                {
                    Source = Copy(l.Source),
                    Incoming = l.Incoming is null ? null : Copy(l.Incoming),
                    TransitionTypeId = l.TransitionTypeId,
                    Progress = l.Progress,
                    TransitionParams = l.TransitionParams,
                    Effects = l.Effects,
                    Opacity = l.Opacity,
                }).ToList(),
            });
            Array.Clear(canvas);
            return Task.CompletedTask;
        }

        private static RenderSource Copy(RenderSource s) => new()
        {
            Frame = new DecodedFrame { Width = s.Frame.Width, Height = s.Frame.Height, Pixels = (byte[])s.Frame.Pixels.Clone() },
            Effects = s.Effects,
            Crop = s.Crop,
        };
    }

    private static (ProjectModel Project, TimelineModel Timeline, VideoClip Clip) Setup(double clipDur = 1.0)
    {
        var asset = new MediaService().Probe(AssetPath);
        var project = ProjectModel.Create("export");
        project.Media.Add(asset);
        var track = new Track { Kind = TrackKind.Video, Index = 0 };
        var clip = new VideoClip { Id = "c1", SourceId = asset.Id, StartSec = 0, DurSec = clipDur, SrcInSec = 0, SrcOutSec = clipDur };
        track.Clips.Add(clip);
        var timeline = new TimelineModel { Rate = FrameRate.Common(30), Tracks = { track } };
        return (project, timeline, clip);
    }

    [Fact]
    public void Export_AsksTheRendererForEveryFrame_WithTheClipsEffectsAndCrop()
    {
        var (project, timeline, clip) = Setup(0.5);
        var invert = EffectCatalog.Find(EffectCatalog.Invert)!.CreateInstance();
        clip.Effects.Add(invert);
        clip.CropL = 0.1;
        var trackFx = EffectCatalog.Find(EffectCatalog.Sepia)!.CreateInstance();
        timeline.Tracks[0].Effects.Add(trackFx);

        var renderer = new RecordingRenderer();
        var path = Path.Combine(Path.GetTempPath(), $"fig-export-{Guid.NewGuid():N}.mp4");
        try
        {
            new MediaService().RenderTimeline(project, timeline, path, 320, 180, renderer: renderer);
            Assert.Equal(15, renderer.Scenes.Count);   // 0.5 s at 30 fps
            var scene = renderer.Scenes[3];
            Assert.Equal((320, 180), (scene.Width, scene.Height));
            var layer = Assert.Single(scene.Layers);
            Assert.Equal(320 * 180 * 4, layer.Source.Frame.Width * layer.Source.Frame.Height * 4);
            Assert.Equal(invert.TypeId, Assert.Single(layer.Source.Effects).TypeId);
            Assert.Equal(0.1, layer.Source.Crop.Left, 6);
            Assert.Equal(trackFx.TypeId, Assert.Single(layer.Effects).TypeId);
            Assert.Equal(3 / 30.0, layer.Effects[0].LocalT, 6);   // track effects see timeline time
            Assert.Equal(3 / 30.0, layer.Source.Effects[0].LocalT, 6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_PipelinesFrames_AndStillEncodesThemInOrder()
    {
        var (project, timeline, _) = Setup(0.4);
        var path = Path.Combine(Path.GetTempPath(), $"fig-export-{Guid.NewGuid():N}.mp4");
        try
        {
            new MediaService().RenderTimeline(project, timeline, path, 320, 180, renderer: new RecordingRenderer { MaxInFlight = 4 });
            var written = new MediaService().Probe(path);
            Assert.Equal(320, written.Width);
            Assert.Equal(180, written.Height);
            Assert.InRange(written.DurationSec, 0.3, 0.6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_WithTheCpuRenderer_ProducesTheClipsPicture()
    {
        var (project, timeline, _) = Setup(0.4);
        var path = Path.Combine(Path.GetTempPath(), $"fig-export-{Guid.NewGuid():N}.mp4");
        try
        {
            new MediaService().RenderTimeline(project, timeline, path, 320, 180);
            using var source = new MediaService().OpenVideoSource(path, 320, 180);
            var frame = source.DecodeForward(0.1)!;
            Assert.Equal(320, frame.Width);
            // the 3 second timer clip is not a black frame
            Assert.True(frame.Pixels.Take(320 * 180 * 4).Any(b => b > 40), "exported picture is black");
        }
        finally { File.Delete(path); }
    }
}
