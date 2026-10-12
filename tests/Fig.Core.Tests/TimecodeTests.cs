using Fig.Core.Timeline;

namespace Fig.Core.Tests;

public class TimecodeTests
{
    private static readonly FrameRate Fps30 = new(30, 1);

    [Fact]
    public void Format_UsesHoursMinutesSecondsFrames()
    {
        Assert.Equal("00:00:04:00", Timecode.Format(4, Fps30));
        Assert.Equal("00:01:05:15", Timecode.Format(65.5, Fps30));
        Assert.Equal("01:00:00:00", Timecode.Format(3600, Fps30));
    }

    [Theory]
    [InlineData("00:00:04:00", 4)]
    [InlineData("4", 4)]
    [InlineData("4.5", 4.5)]
    [InlineData("01:05:15", 65.5)]
    [InlineData("00:00:00:15", 0.5)]
    public void TryParse_AcceptsCommonForms(string text, double expected)
    {
        Assert.True(Timecode.TryParse(text, Fps30, out var seconds));
        Assert.Equal(expected, seconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:2:3:4:5")]
    [InlineData("-3")]
    public void TryParse_RejectsGarbage(string text)
        => Assert.False(Timecode.TryParse(text, Fps30, out _));
}
