using System;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Declares an effect's catalog metadata on its implementation class. The catalog is
    /// built by discovering classes carrying this attribute — no separate registration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EffectAttribute : Attribute
    {
        public new string TypeId { get; }
        public string DisplayName { get; }
        public string Icon { get; set; } = "wand-sparkles";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "Stylize";
        public EffectKind Kind { get; set; } = EffectKind.Video;

        public EffectAttribute(string typeId, string displayName)
        {
            TypeId = typeId;
            DisplayName = displayName;
        }
    }

    /// <summary>Declares one parameter on an effect's implementation class.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class EffectParamAttribute(string key, string label) : ParamAttribute(key, label);
}
