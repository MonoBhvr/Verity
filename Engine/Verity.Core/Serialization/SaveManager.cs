using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verity.Core.Serialization;

[JsonConverter(typeof(SaveDataJsonConverter))]
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Dictionary<string, object?> Data { get; set; } = new(StringComparer.Ordinal);

    public object? this[string key]
    {
        get => Data[key];
        set => Data[key] = value;
    }

    public void Set(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Data[key] = value;
    }

    public T Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!Data.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Save data does not contain key '{key}'.");

        return ConvertValue<T>(value);
    }

    public bool TryGet<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!Data.TryGetValue(key, out var rawValue))
        {
            value = default;
            return false;
        }

        value = ConvertValue<T>(rawValue);
        return true;
    }

    private static T ConvertValue<T>(object? value)
    {
        if (value is T typedValue)
            return typedValue;

        if (value is null)
            return default!;

        if (value is JsonElement element)
            return JsonSerializer.Deserialize<T>(element.GetRawText(), SaveManager.JsonOptions)!;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (targetType.IsEnum && value is string enumName)
            return (T)Enum.Parse(targetType, enumName, ignoreCase: true);

        if (value is IConvertible)
            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

        var json = JsonSerializer.Serialize(value, SaveManager.JsonOptions);
        return JsonSerializer.Deserialize<T>(json, SaveManager.JsonOptions)!;
    }
}

public static class SaveManager
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string SaveDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "Saves");

    public static void Save(int slot, SaveData data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(SaveDirectory);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(GetSavePath(slot), json);
    }

    public static SaveData Load(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        var path = GetSavePath(slot);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No save exists for slot {slot}.", path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SaveData>(json, JsonOptions) ?? new SaveData();
    }

    public static bool HasSave(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        return File.Exists(GetSavePath(slot));
    }

    public static void DeleteSave(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        var path = GetSavePath(slot);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static int[] GetUsedSlots()
    {
        if (!Directory.Exists(SaveDirectory))
            return [];

        return Directory.EnumerateFiles(SaveDirectory, "slot-*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(static name => name?[5..])
            .Where(static suffix => int.TryParse(suffix, out _))
            .Select(static suffix => int.Parse(suffix!, CultureInfo.InvariantCulture))
            .OrderBy(static slot => slot)
            .ToArray();
    }

    private static string GetSavePath(int slot) => Path.Combine(SaveDirectory, $"slot-{slot}.json");
}

internal sealed class SaveDataJsonConverter : JsonConverter<SaveData>
{
    public override SaveData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var saveData = new SaveData
        {
            Version = root.TryGetProperty("Version", out var versionElement)
                ? versionElement.GetInt32()
                : SaveData.CurrentVersion
        };

        if (root.TryGetProperty("Data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            saveData.Data = ReadObject(dataElement);

        return saveData;
    }

    public override void Write(Utf8JsonWriter writer, SaveData value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Version", value.Version);
        writer.WritePropertyName("Data");
        WriteValue(writer, value.Data, options);
        writer.WriteEndObject();
    }

    private static Dictionary<string, object?> ReadObject(JsonElement element)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
            dictionary[property.Name] = ReadValue(property.Value);

        return dictionary;
    }

    private static object? ReadValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ReadObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                return;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                return;
            case IDictionary dictionary:
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is not string key)
                        continue;

                    writer.WritePropertyName(key);
                    WriteValue(writer, entry.Value, options);
                }
                writer.WriteEndObject();
                return;
            case IEnumerable enumerable:
                writer.WriteStartArray();
                foreach (var item in enumerable)
                    WriteValue(writer, item, options);
                writer.WriteEndArray();
                return;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                return;
        }
    }
}
