using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class TimelineViewportTests
{
    [Fact]
    public void TimeToX_Scrolls_And_Scales()
    {
        var vp = new TimelineViewport();
        vp.SetScrollTime(10);
        vp.SetPixelsPerSecond(100);

        Assert.Equal(0, vp.TimeToX(10));    // left edge
        Assert.Equal(200, vp.TimeToX(12));  // 2s in
        Assert.Equal(10, vp.XToTime(0));    // inverse
        Assert.Equal(12, vp.XToTime(200));
    }

    [Fact]
    public void XToTime_IsInverseOf_TimeToX()
    {
        var vp = new TimelineViewport();
        vp.SetScrollTime(5);
        vp.SetPixelsPerSecond(50);

        for (var x = 0.0; x <= 1000; x += 100)
        {
            var time = vp.XToTime(x);
            Assert.Equal(x, vp.TimeToX(time), 3);
        }
    }

    [Fact]
    public void ZoomAt_KeepsCursorTimeStationary()
    {
        var vp = new TimelineViewport();
        vp.SetScrollTime(4);
        vp.SetPixelsPerSecond(100);

        var cursorX = 300.0;
        var timeBefore = vp.XToTime(cursorX);   // 4 + 300/100 = 7s

        vp.ZoomAt(cursorX, 2.0);

        Assert.Equal(200, vp.PixelsPerSecond);
        var timeAfter = vp.XToTime(cursorX);
        Assert.Equal(timeBefore, timeAfter, 3);   // time under cursor unchanged
    }

    [Fact]
    public void ZoomAt_ClampsToMaxPps()
    {
        var vp = new TimelineViewport();
        vp.SetPixelsPerSecond(1900);

        vp.ZoomAt(0, 2.0);

        Assert.Equal(TimelineViewport.MaxPixelsPerSecond, vp.PixelsPerSecond);
    }

    [Fact]
    public void ScrollBy_MovesViewport()
    {
        var vp = new TimelineViewport();
        vp.ScrollBy(5);
        Assert.Equal(5, vp.ScrollTime);

        vp.ScrollBy(-20);   // cannot scroll before 0
        Assert.Equal(0, vp.ScrollTime);
    }

    [Fact]
    public void VisibleEndTime_TracksScrollAndZoom()
    {
        var vp = new TimelineViewport();
        vp.SetScrollTime(2);
        vp.SetPixelsPerSecond(100);

        Assert.Equal(8, vp.VisibleEndTime(600), 3);   // 2 + 600/100
    }
}

public class RulerCalculatorTests
{
    [Theory]
    [InlineData(10, 30, 15, 3)]       // zoomed out: 15 s majors
    [InlineData(100, 30, 2, 4)]       // 100 px/s: a label every 2 s, ticks every half second
    [InlineData(2000, 30, 2 / 30.0, 2)] // zoomed in: majors land on whole frames
    public void PickScale_AdaptsToZoom_AndLandsOnNiceValues(double pps, double fps, double major, int divisions)
    {
        var scale = RulerCalculator.PickScale(pps, fps);
        Assert.Equal(major, scale.MajorSec, 6);
        Assert.Equal(divisions, scale.Divisions);
        Assert.Equal(scale.MajorSec / divisions, scale.MinorSec, 9);
        Assert.True(scale.MajorSec * pps >= 104);   // room for a timecode label
    }
}
