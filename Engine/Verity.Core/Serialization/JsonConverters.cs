using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verity.Core.World;

namespace Verity.Core.Serialization;

public class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        float x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vector2(x, y);
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString()?.ToUpperInvariant() ?? "";
                reader.Read();
                if (propertyName == "X") x = reader.GetSingle();
                else if (propertyName == "Y") y = reader.GetSingle();
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteEndObject();
    }
}

public class ColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        float r = 1, g = 1, b = 1, a = 1;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Color(r, g, b, a);
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString()?.ToUpperInvariant() ?? "";
                reader.Read();
                if (propertyName == "R") r = reader.GetSingle();
                else if (propertyName == "G") g = reader.GetSingle();
                else if (propertyName == "B") b = reader.GetSingle();
                else if (propertyName == "A") a = reader.GetSingle();
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("R", value.R);
        writer.WriteNumber("G", value.G);
        writer.WriteNumber("B", value.B);
        writer.WriteNumber("A", value.A);
        writer.WriteEndObject();
    }
}
