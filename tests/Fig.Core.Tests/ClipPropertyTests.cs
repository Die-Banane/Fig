using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class ClipPropertyTests
{
    [Fact]
    public void SettingAProperty_SetsItsValue_OrKeysThePlayheadOnceItIsAnimated()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("v", 10, 4);
        editor.AddClip(track.Id, clip);
        var opacity = ClipProperties.Opacity;

        editor.SetClipProperty([clip.Id], opacity, ParamValue.OfDouble(0.4), playheadSec: 11);
        Assert.Equal(0.4, clip.Opacity, 9);
        Assert.False(opacity.IsAnimated(clip));

        // animated: a slider drag at the playhead reshapes the key there, as one undo step
        editor.SetClipKeyframe(clip.Id, opacity.Key, 0, 1);
        foreach (var v in new[] { 0.3, 0.2, 0.1 })
            editor.SetClipProperty([clip.Id], opacity, ParamValue.OfDouble(v), playheadSec: 12);
        Assert.Equal(0.4, clip.Opacity, 9);                          // the base value is left alone
        Assert.Equal(0.1, opacity.NumberAt(clip, 2), 9);
        Assert.Equal(0.55, opacity.NumberAt(clip, 1), 9);            // halfway between the keys
        editor.Undo();
        Assert.Single(clip.Keyframes[opacity.Key]);
    }

    [Fact]
    public void EveryProperty_ReadsBackWhatItWrites_OnTheClipsThatHaveIt()
    {
        Clip[] clips = [TimelineFixtures.Video("v", 0, 4), new TextClip { DurSec = 4 }];
        foreach (var property in ClipProperties.All)
            foreach (var clip in clips.Where(property.Has))
            {
                var value = property.Def.Kind switch
                {
                    ParamKind.Bool => ParamValue.OfBool(!property.Get(clip).AsBool),
                    ParamKind.Color => ParamValue.OfColor(0xFF123456),
                    ParamKind.List => ParamValue.OfChoice(property.Def.Choices.Count - 1),
                    _ => ParamValue.OfDouble(property.Def.Min + (property.Def.Max - property.Def.Min) * 0.25),
                };
                property.Set(clip, value);
                Assert.Equal(value, property.Get(clip));
            }
    }
}
