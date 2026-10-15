using System.Collections.Generic;

namespace Fig.Core.Timeline;

/// <summary>The timing domain and stack edited by the effect inspector. Track times are absolute.</summary>
public interface IEffectOwner
{
    string Id { get; }
    double StartSec { get; }
    double DurSec { get; }
    List<EffectInstance> Effects { get; }
}
