using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fig.Core.Timeline
{
    /// <summary>Kind of a parameter value, driving both the schema and the editor control.</summary>
    public enum ParamKind
    {
        Double,
        Int,
        Bool,
        Color,
        List,
    }

    /// <summary>
    /// A typed parameter value (double / int / bool / color / list choice), validated against
    /// the owning schema. Keyframe tracks wrap the same value type.
    /// </summary>
    [JsonConverter(typeof(ParamValueJsonConverter))]
    public readonly struct ParamValue
    {
        public ParamKind Kind { get; }
        private readonly double _number;
        private readonly bool _bool;
        private readonly uint _color;
        private readonly int _choice;

        private ParamValue(ParamKind kind, double number, bool b, uint color, int choice)
        {
            Kind = kind;
            _number = number;
            _bool = b;
            _color = color;
            _choice = choice;
        }

        public static ParamValue OfDouble(double v) => new(ParamKind.Double, v, false, 0, 0);
        public static ParamValue OfInt(int v) => new(ParamKind.Int, v, false, 0, 0);
        public static ParamValue OfBool(bool v) => new(ParamKind.Bool, 0, v, 0, 0);
        public static ParamValue OfColor(uint argb) => new(ParamKind.Color, 0, false, argb, 0);
        public static ParamValue OfChoice(int index) => new(ParamKind.List, 0, false, 0, index);

        public double AsDouble => Kind == ParamKind.Double ? _number : throw new InvalidOperationException($"ParamValue is {Kind}, not Double");
        public int AsInt => Kind == ParamKind.Int ? (int)_number : throw new InvalidOperationException($"ParamValue is {Kind}, not Int");
        public bool AsBool => Kind == ParamKind.Bool ? _bool : throw new InvalidOperationException($"ParamValue is {Kind}, not Bool");
        public uint AsColor => Kind == ParamKind.Color ? _color : throw new InvalidOperationException($"ParamValue is {Kind}, not Color");
        public int AsChoice => Kind == ParamKind.List ? _choice : throw new InvalidOperationException($"ParamValue is {Kind}, not List");

        /// <summary>Numeric value for Double or Int kinds.</summary>
        public double AsNumber => Kind is ParamKind.Double or ParamKind.Int
            ? _number
            : throw new InvalidOperationException($"ParamValue is {Kind}, not numeric");

        public override bool Equals(object? obj) => obj is ParamValue other && Equals(other);

        public bool Equals(ParamValue other)
            => Kind == other.Kind
            && _number.Equals(other._number)
            && _bool == other._bool
            && _color == other._color
            && _choice == other._choice;

        public override int GetHashCode()
            => HashCode.Combine((int)Kind, _number, _bool, _color, _choice);

        public static bool operator ==(ParamValue a, ParamValue b) => a.Equals(b);
        public static bool operator !=(ParamValue a, ParamValue b) => !a.Equals(b);

        public override string ToString() => Kind switch
        {
            ParamKind.Bool => _bool.ToString(),
            ParamKind.Color => _color.ToString("X8"),
            ParamKind.List => _choice.ToString(),
            ParamKind.Int => ((int)_number).ToString(),
            _ => _number.ToString("0.###"),
        };
    }

    /// <summary>
    /// Serializes <see cref="ParamValue"/> as {"kind": "...", "value": ...}. Reads legacy bare
    /// numbers (and booleans) as the matching kind, so old project files keep loading.
    /// </summary>
    public sealed class ParamValueJsonConverter : JsonConverter<ParamValue>
    {
        public override ParamValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return ParamValue.OfDouble(reader.GetDouble());
            if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                return ParamValue.OfBool(reader.GetBoolean());
            if (reader.TokenType == JsonTokenType.Null)
                return default;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                string? kind = null;
                double number = 0;
                bool b = false;
                uint color = 0;
                int choice = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;
                    var prop = reader.GetString();
                    reader.Read();
                    switch (prop)
                    {
                        case "kind":
                            kind = reader.GetString();
                            break;
                        case "value" when kind == "bool":
                            b = reader.GetBoolean();
                            break;
                        case "value" when kind == "color":
                            color = reader.GetUInt32();
                            break;
                        case "value" when kind == "list":
                            choice = reader.GetInt32();
                            break;
                        case "value":
                            number = reader.GetDouble();
                            break;
                    }
                }
                return kind switch
                {
                    "int" => ParamValue.OfInt((int)number),
                    "bool" => ParamValue.OfBool(b),
                    "color" => ParamValue.OfColor(color),
                    "list" => ParamValue.OfChoice(choice),
                    _ => ParamValue.OfDouble(number),
                };
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, ParamValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind switch
            {
                ParamKind.Int => "int",
                ParamKind.Bool => "bool",
                ParamKind.Color => "color",
                ParamKind.List => "list",
                _ => "double",
            });
            switch (value.Kind)
            {
                case ParamKind.Bool:
                    writer.WriteBoolean("value", value.AsBool);
                    break;
                case ParamKind.Color:
                    writer.WriteNumber("value", value.AsColor);
                    break;
                case ParamKind.List:
                    writer.WriteNumber("value", value.AsChoice);
                    break;
                default:
                    writer.WriteNumber("value", value.AsNumber);
                    break;
            }
            writer.WriteEndObject();
        }
    }

    public sealed class ParamDef
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public ParamKind Kind { get; init; } = ParamKind.Double;
        public double Default { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
        public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

        /// <summary>How the value reads beside its slider ("{0:P0}" for 50%, "{0:0.0} s" for seconds...).</summary>
        public string Format { get; init; } = "{0:0.##}";

        /// <summary>The schema default as a typed <see cref="ParamValue"/>.</summary>
        public ParamValue DefaultValue() => Kind switch
        {
            ParamKind.Int => ParamValue.OfInt((int)Math.Round(Default)),
            ParamKind.Bool => ParamValue.OfBool(Default != 0),
            ParamKind.Color => ParamValue.OfColor((uint)Default),
            ParamKind.List => ParamValue.OfChoice((int)Math.Round(Default)),
            _ => ParamValue.OfDouble(Default),
        };
    }

    /// <summary>
    /// One parameter declared on an implementation class (an effect's, a transition's, a graph primitive's). Each
    /// catalog has its own attribute deriving from this, so it reads its own; they all become a <see cref="ParamDef"/>
    /// the same way.
    /// </summary>
    public abstract class ParamAttribute : Attribute
    {
        protected ParamAttribute(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
        public ParamKind Kind { get; set; } = ParamKind.Double;
        public double Default { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public string[] Choices { get; set; } = Array.Empty<string>();

        /// <summary>How the value reads beside its slider; see <see cref="ParamDef.Format"/>.</summary>
        public string Format { get; set; } = "{0:0.##}";

        public ParamDef ToDef() => new()
        {
            Key = Key,
            Label = Label,
            Kind = Kind,
            Default = Default,
            Min = Min,
            Max = Max,
            Choices = Choices,
            Format = Format,
        };
    }
}
