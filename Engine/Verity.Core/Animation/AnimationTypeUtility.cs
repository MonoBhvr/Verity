using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verity.Core;

namespace Verity.Core.Animation;

public static class AnimationTypeUtility
{
    public static string GetTypeName(Type type) => type.FullName ?? type.Name;

    public static bool IsAnimatable(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(float) ||
               type == typeof(int) ||
               type == typeof(bool) ||
               type == typeof(string) ||
               type == typeof(Verity.Core.Vector2) ||
               type == typeof(System.Numerics.Vector2) ||
               type == typeof(Verity.Core.Vector3) ||
               type == typeof(System.Numerics.Vector3) ||
               type == typeof(Vector4) ||
               type == typeof(Color) ||
               type == typeof(Sprite) ||
               type.IsEnum;
    }

    public static bool IsInterpolatedType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type == typeof(float) ||
               type == typeof(int) ||
               type == typeof(Verity.Core.Vector2) ||
               type == typeof(System.Numerics.Vector2) ||
               type == typeof(Verity.Core.Vector3) ||
               type == typeof(System.Numerics.Vector3) ||
               type == typeof(Vector4) ||
               type == typeof(Color);
    }

    public static Type? ResolveType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        return typeName switch
        {
            "float" or "Single" or "System.Single" => typeof(float),
            "int" or "Int32" or "System.Int32" => typeof(int),
            "bool" or "Boolean" or "System.Boolean" => typeof(bool),
            "string" or "String" or "System.String" => typeof(string),
            _ => ResolveComplexType(typeName)
        };
    }

    public static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (targetType.IsInstanceOfType(value))
            return value;

        if (value is JsonElement element)
            return ConvertJsonElement(element, targetType);

        if (value is JsonNode node)
        {
            using var doc = JsonDocument.Parse(node.ToJsonString());
            return ConvertJsonElement(doc.RootElement, targetType);
        }

        if (targetType == typeof(Sprite) && value is string spritePath)
            return new Sprite(spritePath);

        if (targetType.IsEnum)
        {
            if (value is string enumName)
                return Enum.Parse(targetType, enumName, ignoreCase: true);

            if (value is JsonElement enumElement)
            {
                if (enumElement.ValueKind == JsonValueKind.String)
                    return Enum.Parse(targetType, enumElement.GetString() ?? string.Empty, ignoreCase: true);

                if (enumElement.ValueKind == JsonValueKind.Number)
                    return Enum.ToObject(targetType, enumElement.GetInt32());
            }
        }

        try
        {
            if (value is IConvertible)
                return Convert.ChangeType(value, targetType);
        }
        catch
        {
        }

        return value;
    }

    public static object? CloneValue(object? value)
    {
        if (value is JsonElement element)
            return element.Clone();

        return value;
    }

    private static Type? ResolveComplexType(string typeName)
    {
        var exact = Type.GetType(typeName, throwOnError: false);
        if (exact != null)
            return exact;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = asm.GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }
            catch
            {
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.Name == typeName || type.FullName == typeName)
                        return type;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        try
        {
            if (targetType == typeof(float))
                return element.GetSingle();

            if (targetType == typeof(int))
                return element.GetInt32();

            if (targetType == typeof(bool))
                return element.GetBoolean();

            if (targetType == typeof(string))
                return element.GetString() ?? string.Empty;

            if (targetType == typeof(Verity.Core.Vector2))
                return new Verity.Core.Vector2(ReadNumber(element, "X"), ReadNumber(element, "Y"));

            if (targetType == typeof(System.Numerics.Vector2))
                return new System.Numerics.Vector2(ReadNumber(element, "X"), ReadNumber(element, "Y"));

            if (targetType == typeof(Verity.Core.Vector3))
                return new Verity.Core.Vector3(ReadNumber(element, "X"), ReadNumber(element, "Y"), ReadNumber(element, "Z"));

            if (targetType == typeof(System.Numerics.Vector3))
                return new System.Numerics.Vector3(ReadNumber(element, "X"), ReadNumber(element, "Y"), ReadNumber(element, "Z"));

            if (targetType == typeof(Vector4))
                return new Vector4(ReadNumber(element, "X"), ReadNumber(element, "Y"), ReadNumber(element, "Z"), ReadNumber(element, "W"));

            if (targetType == typeof(Color))
                return new Color(ReadNumber(element, "R", 1f), ReadNumber(element, "G", 1f), ReadNumber(element, "B", 1f), ReadNumber(element, "A", 1f));

            if (targetType == typeof(Sprite))
            {
                if (element.ValueKind == JsonValueKind.String)
                    return new Sprite(element.GetString() ?? string.Empty);

                return new Sprite(ReadString(element, "Path"));
            }

            if (targetType.IsEnum)
            {
                if (element.ValueKind == JsonValueKind.String)
                    return Enum.Parse(targetType, element.GetString() ?? string.Empty, ignoreCase: true);

                if (element.ValueKind == JsonValueKind.Number)
                    return Enum.ToObject(targetType, element.GetInt32());
            }

            return JsonSerializer.Deserialize(element.GetRawText(), targetType);
        }
        catch
        {
            return null;
        }
    }

    private static float ReadNumber(JsonElement element, string propertyName, float fallback = 0f)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var child)
            ? child.GetSingle()
            : fallback;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var child)
            ? child.GetString() ?? string.Empty
            : string.Empty;
    }
}
