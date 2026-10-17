using System;
using System.Collections.Generic;

namespace Fig.Core.Timeline
{
    public static class ClipFactory
    {
        /// <summary>A copy of the clip with a new id (see <see cref="Clip.Clone"/>).</summary>
        public static Clip Clone(Clip source) => source.Clone();

        public static Clip CloneWithRange(Clip source, double startSec, double durSec, double srcInSec, double srcOutSec)
        {
            var clone = Clone(source);
            clone.StartSec = startSec;
            clone.DurSec = durSec;
            SetSourceRange(clone, srcInSec, srcOutSec);
            return clone;
        }

        /// <summary>A new clip of the part of <paramref name="clip"/> before <paramref name="offset"/> (seconds into it).</summary>
        public static Clip Before(Clip clip, double offset)
        {
            var left = CloneWithRange(clip, clip.StartSec, offset, clip.SourceIn, ClipTime.SourceAt(clip, offset));
            ClipTime.KeepBefore(left, offset);
            ClipFade.ApplySplitLeft(left);
            return left;
        }

        /// <summary>A new clip of the part of <paramref name="clip"/> from <paramref name="offset"/> (seconds into it) on.</summary>
        public static Clip After(Clip clip, double offset)
        {
            var right = CloneWithRange(clip, clip.StartSec + offset, clip.DurSec - offset, ClipTime.SourceAt(clip, offset), clip.SourceOut);
            ClipTime.KeepAfter(right, offset);
            ClipFade.ApplySplitRight(right);
            return right;
        }

        public static void SetSourceRange(Clip clip, double srcInSec, double srcOutSec) => clip.SetSourceRange(srcInSec, srcOutSec);
    }
}
