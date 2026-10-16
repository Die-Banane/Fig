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

        public static void SetSourceRange(Clip clip, double srcInSec, double srcOutSec) => clip.SetSourceRange(srcInSec, srcOutSec);
    }
}
