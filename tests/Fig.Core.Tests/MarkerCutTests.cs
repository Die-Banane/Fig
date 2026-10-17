using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class MarkerCutTests
{
    [Fact]
    public void AudioClipMarkers_CutCorrespondingVideoAndLinkedAudio_InOneUndoStep()
    {
        var (editor, videoTrack) = TimelineFixtures.Create();
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        var musicTrack = editor.AddTrack(TrackKind.Audio);
        var video = TimelineFixtures.Video("video", 8, 10, 4);
        video.LinkGroupId = "pair";
        var audio = new AudioClip { StartSec = 8, DurSec = 10, SrcInSec = 4, SrcOutSec = 14, LinkGroupId = "pair" };
        var music = new AudioClip { StartSec = 10, DurSec = 5, SrcOutSec = 5 };
        editor.AddClip(videoTrack.Id, video);
        editor.AddClip(audioTrack.Id, audio);
        editor.AddClip(musicTrack.Id, music);
        var first = editor.AddMarker(music, 1);
        var last = editor.AddMarker(music, 3);
        editor.Selection.SelectMarkers(new[] { first.Id, last.Id });

        Assert.Equal(2, editor.SplitVideoAtSelectedMarkers());
        Assert.Equal(new[] { 8.0, 11.0, 13.0 }, videoTrack.Clips.Select(c => c.StartSec));
        Assert.Equal(new[] { 3.0, 2.0, 5.0 }, videoTrack.Clips.Select(c => c.DurSec));
        Assert.Single(musicTrack.Clips);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(videoTrack.Clips[i].LinkGroupId, audioTrack.Clips[i].LinkGroupId);
            Assert.Equal(videoTrack.Clips[i].SourceIn, audioTrack.Clips[i].SourceIn);
        }
        Assert.Equal(3, videoTrack.Clips.Select(c => c.LinkGroupId).Distinct().Count());
        var rightIds = videoTrack.Clips.Select(c => c.Id).ToArray();
        editor.Undo();
        Assert.Same(video, Assert.Single(videoTrack.Clips));
        Assert.Same(audio, Assert.Single(audioTrack.Clips));
        Assert.Equal(10, video.DurSec);
        Assert.Equal(14, video.SrcOutSec);
        editor.Redo();
        Assert.Equal(rightIds, videoTrack.Clips.Select(c => c.Id));
    }

    [Fact]
    public void MarkerCuts_DeduplicateFramePositions_SkipEdgesAndGaps_AndLeaveTextAlone()
    {
        var (editor, videoTrack) = TimelineFixtures.Create();
        var overlay = editor.AddTrack(TrackKind.Video);
        editor.AddClip(videoTrack.Id, TimelineFixtures.Video("video", 0, 5));
        editor.AddClip(overlay.Id, new TextClip { StartSec = 0, DurSec = 10, Text = "Title" });
        var markers = new[] { 0.0, 2.001, 2.002, 5.0, 8.0 }
            .Select(t => editor.AddMarker(editor.Document, t)).ToArray();
        editor.Selection.SelectMarkers(markers.Select(m => m.Id));
        Assert.Equal(1, editor.SplitVideoAtSelectedMarkers());
        Assert.Equal(2, videoTrack.Clips.Count);
        Assert.Single(overlay.Clips);
        Assert.Equal(0, editor.SplitVideoAtSelectedMarkers());
        editor.Undo(); // the no-op must not add a history entry
        Assert.Single(videoTrack.Clips);
    }

    [Fact]
    public void ClipMarkers_KeepTheirIdsAndSelection_AcrossCutsUndoAndRedo()
    {
        var (editor, track) = TimelineFixtures.Create();
        var clip = TimelineFixtures.Video("video", 10, 10);
        editor.AddClip(track.Id, clip);
        var a = editor.AddMarker(clip, 2);
        var b = editor.AddMarker(clip, 6);
        editor.Selection.SelectMarkers(new[] { a.Id, b.Id });
        Assert.Equal(2, editor.SplitVideoAtSelectedMarkers());
        Assert.Equal(12, editor.FindMarker(a.Id)!.Clip!.StartSec + editor.FindMarker(a.Id)!.Marker.StartSec);
        Assert.Equal(16, editor.FindMarker(b.Id)!.Clip!.StartSec + editor.FindMarker(b.Id)!.Marker.StartSec);
        Assert.Equal(2, editor.Selection.SelectedMarkerIds.Count);
        editor.Undo();
        Assert.Equal(new[] { a.Id, b.Id }, clip.Markers.Select(m => m.Id));
        editor.Redo();
        Assert.NotNull(editor.FindMarker(a.Id));
        Assert.NotNull(editor.FindMarker(b.Id));
    }

    [Fact]
    public void Cuts_RespectSpeed_WhateverTheSourceFrameRate()
    {
        var (editor, track) = TimelineFixtures.Create(30);
        var clip = TimelineFixtures.Video("video", 0, 6, 10);
        clip.Speed = 2;
        clip.SourceRate = new FrameRate(25, 1);
        clip.SrcOutSec = 20;
        editor.AddClip(track.Id, clip);
        var marker = editor.AddMarker(editor.Document, 3);
        editor.Selection.SelectMarker(marker.Id);
        Assert.Equal(1, editor.SplitVideoAtSelectedMarkers());
        Assert.Equal(16, track.Clips[0].SourceOut, 6);
        Assert.Equal(16, track.Clips[1].SourceIn, 6);
        editor.Undo();
        Assert.Equal(10, clip.SourceIn);
        Assert.Equal(20, clip.SourceOut);
    }
}
