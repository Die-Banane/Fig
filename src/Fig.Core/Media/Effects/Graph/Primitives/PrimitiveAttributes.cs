using System;
using Fig.Core.Timeline;

namespace Fig.Core.Media
{
    /// <summary>
    /// Declares a graph primitive's catalog metadata on its implementation class. Like effects,
    /// primitives are discovered: write the class with its descriptors and it appears in the editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PrimitiveAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Category { get; set; } = "Other";
        public string Description { get; set; } = "";

        public PrimitiveAttribute(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PrimitiveInputAttribute : Attribute
    {
        public string Id { get; }
        public string Label { get; }
        public GraphPortType Type { get; }
        public bool Required { get; set; } = true;

        public PrimitiveInputAttribute(string id, string label, GraphPortType type = GraphPortType.Image)
        {
            Id = id;
            Label = label;
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PrimitiveOutputAttribute : Attribute
    {
        public string Id { get; }
        public string Label { get; }
        public GraphPortType Type { get; }

        public PrimitiveOutputAttribute(string id, string label, GraphPortType type = GraphPortType.Image)
        {
            Id = id;
            Label = label;
            Type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class PrimitiveParamAttribute(string key, string label) : ParamAttribute(key, label);
}
