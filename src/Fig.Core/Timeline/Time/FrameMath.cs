using System;
using Fig.Core.Timeline;

namespace Fig.Core.Timeline
{
    public static class FrameMath
    {

        public static double SnapToFrame(double sec, FrameRate rate)
        {
            return rate.ToSeconds(rate.ToFrame(sec));
        }
    }
}
