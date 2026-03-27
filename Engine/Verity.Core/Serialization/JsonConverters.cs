using System.Numerics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verity.Core.Audio;
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

public class Vector3Converter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        float x = 0, y = 0, z = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vector3(x, y, z);
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString()?.ToUpperInvariant() ?? "";
                reader.Read();
                if (propertyName == "X") x = reader.GetSingle();
                else if (propertyName == "Y") y = reader.GetSingle();
                else if (propertyName == "Z") z = reader.GetSingle();
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Z", value.Z);
        writer.WriteEndObject();
    }
}

public class Vector4Converter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        float x = 0, y = 0, z = 0, w = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new Vector4(x, y, z, w);
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString()?.ToUpperInvariant() ?? "";
                reader.Read();
                if (propertyName == "X") x = reader.GetSingle();
                else if (propertyName == "Y") y = reader.GetSingle();
                else if (propertyName == "Z") z = reader.GetSingle();
                else if (propertyName == "W") w = reader.GetSingle();
            }
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Z", value.Z);
        writer.WriteNumber("W", value.W);
        writer.WriteEndObject();
    }
}

public class AudioClipConverter : JsonConverter<AudioClip>
{
    public override AudioClip? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        string path = root.TryGetProperty("Path", out var pathProp) ? pathProp.GetString() ?? string.Empty : string.Empty;
        string guid = root.TryGetProperty("Guid", out var guidProp) ? guidProp.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var clip = new AudioClip
        {
            Name = root.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(path),
            Path = AssetPathUtility.Normalize(path),
            Guid = guid,
            Type = root.TryGetProperty("Type", out var typeProp) && Enum.TryParse<AudioType>(typeProp.GetString(), true, out var parsedType)
                ? parsedType
                : AudioClip.GuessType(path)
        };

        clip.DefaultVolume = root.TryGetProperty("DefaultVolume", out var volumeProp) ? volumeProp.GetSingle() : clip.DefaultVolume;
        clip.DefaultPitch = root.TryGetProperty("DefaultPitch", out var pitchProp) ? pitchProp.GetSingle() : clip.DefaultPitch;
        clip.IsLooping = root.TryGetProperty("IsLooping", out var loopingProp) && loopingProp.GetBoolean();
        return clip;
    }

    public override void Write(Utf8JsonWriter writer, AudioClip value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WriteString("Path", AssetPathUtility.Normalize(value.Path));
        writer.WriteString("Guid", string.IsNullOrWhiteSpace(value.Guid) ? AssetPathUtility.TryGetGuid(value.Path) : value.Guid);
        writer.WriteString("Type", value.Type.ToString());
        writer.WriteNumber("DefaultVolume", value.DefaultVolume);
        writer.WriteNumber("DefaultPitch", value.DefaultPitch);
        writer.WriteBoolean("IsLooping", value.IsLooping);
        writer.WriteEndObject();
    }
}

public abstract class PathAssetConverter<T> : JsonConverter<T> where T : struct, IPathAsset
{
    protected abstract T Create(string path, string guid);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string path = reader.GetString() ?? string.Empty;
            return Create(path, string.Empty);
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        string pathValue = doc.RootElement.TryGetProperty("Path", out var pathProp) ? pathProp.GetString() ?? string.Empty : string.Empty;
        string guidValue = doc.RootElement.TryGetProperty("Guid", out var guidProp) ? guidProp.GetString() ?? string.Empty : string.Empty;
        return Create(pathValue, guidValue);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Path", AssetPathUtility.Normalize(value.Path));
        writer.WriteString("Guid", string.IsNullOrWhiteSpace(value.Guid) ? AssetPathUtility.TryGetGuid(value.Path) : value.Guid);
        writer.WriteEndObject();
    }
}

public sealed class SpriteConverter : PathAssetConverter<Sprite>
{
    protected override Sprite Create(string path, string guid) => new(path, guid);

    public override Sprite Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string path = reader.GetString() ?? string.Empty;
            return new Sprite(path, string.Empty, string.Empty);
        }

        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        string pathValue = doc.RootElement.TryGetProperty("Path", out var pathProp) ? pathProp.GetString() ?? string.Empty : string.Empty;
        string guidValue = doc.RootElement.TryGetProperty("Guid", out var guidProp) ? guidProp.GetString() ?? string.Empty : string.Empty;
        string spriteId = doc.RootElement.TryGetProperty("SpriteId", out var spriteIdProp) ? spriteIdProp.GetString() ?? string.Empty : string.Empty;
        return new Sprite(pathValue, guidValue, spriteId);
    }

    public override void Write(Utf8JsonWriter writer, Sprite value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Path", AssetPathUtility.Normalize(value.Path));
        writer.WriteString("Guid", string.IsNullOrWhiteSpace(value.Guid) ? AssetPathUtility.TryGetGuid(value.Path) : value.Guid);
        if (!string.IsNullOrWhiteSpace(value.SpriteId))
            writer.WriteString("SpriteId", value.SpriteId);
        writer.WriteEndObject();
    }
}

public sealed class StyleAssetConverter : PathAssetConverter<StyleAsset>
{
    protected override StyleAsset Create(string path, string guid) => new(path, guid);
}

public sealed class ShaderAssetConverter : PathAssetConverter<ShaderAsset>
{
    protected override ShaderAsset Create(string path, string guid) => new(path, guid);
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

public class TileBaseConverter : JsonConverter<TileBase>
{
    public override TileBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("$type", out var typeProp)) return null;
            
            string typeName = typeProp.GetString() ?? "";
            
            // Create a new options object without this converter to avoid infinite recursion
            var innerOptions = new JsonSerializerOptions(options);
            for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
            {
                if (innerOptions.Converters[i] is TileBaseConverter)
                    innerOptions.Converters.RemoveAt(i);
            }

            return typeName switch
            {
                "Tile" => JsonSerializer.Deserialize<Tile>(root.GetRawText(), innerOptions),
                "AnimatedTile" => JsonSerializer.Deserialize<AnimatedTile>(root.GetRawText(), innerOptions),
                "RuleTile" => JsonSerializer.Deserialize<RuleTile>(root.GetRawText(), innerOptions),
                _ => null
            };
        }
    }

    public override void Write(Utf8JsonWriter writer, TileBase value, JsonSerializerOptions options)
    {
        string typeName = value.GetType().Name;
        
        // Create a new options object without this converter to avoid infinite recursion
        var innerOptions = new JsonSerializerOptions(options);
        for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
        {
            if (innerOptions.Converters[i] is TileBaseConverter)
                innerOptions.Converters.RemoveAt(i);
        }

        var node = JsonSerializer.SerializeToNode(value, value.GetType(), innerOptions)?.AsObject();
        if (node != null)
        {
            node["$type"] = typeName;
            node.WriteTo(writer);
        }
    }
}

public class TilemapTilesConverter : JsonConverter<Dictionary<(int x, int y), TileBase>>
{
    public override Dictionary<(int x, int y), TileBase>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected array of tile entries");
        
        var dict = new Dictionary<(int x, int y), TileBase>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject) continue;
            
            int? x = null, y = null;
            TileBase? tile = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string prop = reader.GetString() ?? "";
                    reader.Read();
                    if (prop == "X") x = reader.GetInt32();
                    else if (prop == "Y") y = reader.GetInt32();
                    else if (prop == "Tile") tile = JsonSerializer.Deserialize<TileBase>(ref reader, options);
                }
            }

            if (x.HasValue && y.HasValue && tile != null)
            {
                dict[(x.Value, y.Value)] = tile;
            }
        }
        return dict;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<(int x, int y), TileBase> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var pair in value)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", pair.Key.x);
            writer.WriteNumber("Y", pair.Key.y);
            writer.WritePropertyName("Tile");
            JsonSerializer.Serialize(writer, pair.Value, options);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

