using Fig.Core.Media;
using Fig.Core.Timeline;
using TimelineModel = Fig.Core.Timeline.Timeline;

namespace Fig.Core.Tests;

/// <summary>Everything selectable is one kind of thing: a key, a name, and the facets it can be edited as.</summary>
public class SelectableTests
{
    private static (TimelineEditor Editor, VideoClip Video, AudioClip Audio, Track Track) Setup()
    {
        var track = new Track { Id = "v", Kind = TrackKind.Video };
        var video = new VideoClip { Id = "vc", StartSec = 0, DurSec = 5, SrcOutSec = 5 };
        var audio = new AudioClip { Id = "ac", StartSec = 0, DurSec = 5, SrcOutSec = 5 };
        track.Clips.Add(video);
        var aTrack = new Track { Id = "a", Kind = TrackKind.Audio };
        aTrack.Clips.Add(audio);
        var editor = new TimelineEditor(new TimelineModel { Tracks = { track, aTrack } });
        return (editor, video, audio, track);
    }

    [Fact]
    public void Every_kind_of_clip_says_what_it_can_be_edited_as()
    {
        var (_, video, audio, _) = Setup();
        Assert.True(video.Facets.HasFlag(Facet.Picture | Facet.Sound | Facet.Effects | Facet.Source));
        Assert.False(audio.Facets.HasFlag(Facet.Picture));
        Assert.True(new TextClip().Facets.HasFlag(Facet.Text));
        Assert.False(new TextClip().Facets.HasFlag(Facet.Source));
    }

    [Fact]
    public void Selected_resolves_every_group_to_a_live_object()
    {
        var (editor, video, _, track) = Setup();
        var fx = EffectCatalog.Find(EffectCatalog.Invert)!.CreateInstance();
        editor.AddEffect(video.Id, fx);
        var marker = editor.AddMarker(video, 1, "m");

        editor.Selection.Replace(video.Key);
        Assert.Same(video, Assert.Single(editor.Selected));

        editor.Selection.ActiveTrackId = track.Id;
        Assert.Same(track, Assert.Single(editor.Selected));

        editor.Selection.SelectMarker(marker.Id);
        Assert.Same(marker, Assert.Single(editor.Selected));

        editor.Selection.SelectEffect(video.Id, fx.Id);
        var target = Assert.IsType<EffectTarget>(Assert.Single(editor.Selected));
        Assert.Same(fx, target.Effect);
        Assert.Same(video, target.Owner);
        Assert.Equal("Invert", target.DisplayName);
    }

    [Fact]
    public void A_selection_can_mix_groups()
    {
        var (editor, video, _, track) = Setup();
        editor.Selection.Replace(video.Key);
        editor.Selection.Add(track.Key);   // adding keeps what is there, whatever its group
        Assert.Equal([video.Key, track.Key], editor.Selection.Items);
        editor.Selection.Replace(video.Key);   // a plain pick still replaces everything
        Assert.Equal([video.Key], editor.Selection.Items);
    }

    [Fact]
    public void Shared_facets_are_what_every_selected_item_can_do()
    {
        var (editor, video, audio, _) = Setup();
        editor.Selection.SelectClips([video.Id, audio.Id]);
        var shared = editor.SharedFacets;
        Assert.True(shared.HasFlag(Facet.Sound));
        Assert.True(shared.HasFlag(Facet.Retime));
        Assert.False(shared.HasFlag(Facet.Picture));   // the audio clip has no picture

        editor.Selection.SelectOnly(video.Id);
        Assert.True(editor.SharedFacets.HasFlag(Facet.Picture));

        editor.Selection.Clear();
        Assert.Equal(Facet.None, editor.SharedFacets);
    }

    [Fact]
    public void A_deleted_item_drops_out_of_what_is_selected()
    {
        var (editor, video, _, _) = Setup();
        editor.Selection.Replace(new SelectionKey(SelectionGroup.Clip, "gone"));
        Assert.Empty(editor.Selected);
        editor.Selection.Replace(video.Key);
        Assert.Single(editor.Selected);
    }

    [Fact]
    public void Names_match_what_the_inspector_shows()
    {
        var (_, video, audio, track) = Setup();
        Assert.Equal("Video clip", video.DisplayName);
        Assert.Equal("Audio clip", audio.DisplayName);
        Assert.Equal("Track", track.DisplayName);
        Assert.Equal("Point marker", new Marker().DisplayName);
        Assert.Equal("Range marker", new Marker { DurSec = 2 }.DisplayName);
    }

    [Fact]
    public void Delete_removes_whatever_is_selected_through_one_call()
    {
        var (editor, video, audio, track) = Setup();
        var fx = EffectCatalog.Find(EffectCatalog.Invert)!.CreateInstance();
        editor.AddEffect(video.Id, fx);
        var marker = editor.AddMarker(video, 1, "m");

        editor.Selection.SelectEffect(video.Id, fx.Id);
        Assert.Equal((1, "effect"), editor.DeleteSelection());
        Assert.Empty(video.Effects);

        editor.Selection.SelectMarker(marker.Id);
        Assert.Equal((1, "marker"), editor.DeleteSelection());
        Assert.Empty(video.Markers);

        editor.Selection.SelectOnly(audio.Id);
        Assert.Equal((1, "clip"), editor.DeleteSelection());
        Assert.Empty(editor.Document.Tracks[1].Clips);

        editor.Selection.ActiveTrackId = editor.Document.Tracks[1].Id;
        Assert.Equal((1, "track"), editor.DeleteSelection());
        Assert.Single(editor.Document.Tracks);

        editor.Selection.Clear();
        Assert.Null(editor.DeleteSelection());
        editor.Selection.Replace(new SelectionKey(SelectionGroup.Media, "library-file"));
        Assert.Null(editor.DeleteSelection());   // a library file is not deleted from the timeline
    }

    [Fact]
    public void The_status_bar_text_comes_from_the_selected_things()
    {
        var (editor, video, audio, _) = Setup();
        Assert.Equal("Nothing selected", editor.DescribeSelection().Summary);

        editor.Selection.SelectOnly(video.Id);
        Assert.Equal("1 clip selected", editor.DescribeSelection().Summary);

        editor.Selection.SelectClips([video.Id, audio.Id]);
        Assert.Equal("2 clips selected", editor.DescribeSelection().Summary);

        var fx = EffectCatalog.Find(EffectCatalog.Invert)!.CreateInstance();
        editor.AddEffect(video.Id, fx);
        editor.Selection.SelectEffect(video.Id, fx.Id);
        var (summary, hint) = editor.DescribeSelection();
        Assert.Equal("Effect selected", summary);
        Assert.Contains("Delete removes the effect", hint);
    }
}
