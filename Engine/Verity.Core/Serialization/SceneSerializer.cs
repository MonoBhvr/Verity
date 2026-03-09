using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.Serialization;

public static class SceneSerializer
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static string Serialize(World.World world)
    {
        var rootArray = new JsonArray();
        var flattened = new List<Entity>();
        foreach (var root in world.RootEntities)
            SerializeEntityRecursive(root, rootArray, -1, flattened);
        return rootArray.ToJsonString(_options);
    }

    private static void SerializeEntityRecursive(Entity entity, JsonArray array, int parentIndex, List<Entity> flattened)
    {
        int currentIndex = flattened.Count;
        flattened.Add(entity);

        var entityJson = new JsonObject
        {
            ["Name"] = entity.Name,
            ["Active"] = entity.Active,
            ["ParentIndex"] = parentIndex,
            ["Position"] = SerializeVector2(entity.Transform.Position),
            ["Rotation"] = entity.Transform.Rotation,
            ["Scale"] = SerializeVector2(entity.Transform.Scale)
        };

        var componentsArray = new JsonArray();
        foreach (var component in entity.GetAllComponents())
        {
            if (component is Transform) continue;

            var type = component.GetType();
            var fieldsJson = new JsonObject();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 1. Fields
            foreach (var f in type.GetFields(flags))
            {
                if (ShouldSerializeMember(f))
                {
                    var node = ValueToJsonNode(f.GetValue(component));
                    if (node != null) fieldsJson[f.Name] = node;
                }
            }

            // 2. Properties
            foreach (var p in type.GetProperties(flags))
            {
                if (p.DeclaringType == typeof(Component) || !p.CanRead || !p.CanWrite) continue;
                if (ShouldSerializeMember(p))
                {
                    try {
                        var node = ValueToJsonNode(p.GetValue(component));
                        if (node != null) fieldsJson[p.Name] = node;
                    } catch { }
                }
            }

            componentsArray.Add(new JsonObject
            {
                ["Type"] = type.FullName ?? type.Name,
                ["Fields"] = fieldsJson
            });
        }

        entityJson["Components"] = componentsArray;
        array.Add(entityJson);

        foreach (var child in entity.Transform.Children)
            SerializeEntityRecursive(child.Owner, array, currentIndex, flattened);
    }

    private static bool ShouldSerializeMember(MemberInfo m)
    {
        // For cross-assembly safety, check attribute names as strings
        var attrs = m.GetCustomAttributes(true);
        foreach (var attr in attrs)
        {
            var attrName = attr.GetType().Name;
            if (attrName == "HideInInspectorAttribute") return false;
            if (attrName == "SerializeFieldAttribute") return true;
        }
        
        // Default visibility
        if (m is FieldInfo f) return f.IsPublic;
        if (m is PropertyInfo p) return (p.GetGetMethod()?.IsPublic ?? false) && (p.GetSetMethod()?.IsPublic ?? false);
        return false;
    }

    private static JsonNode? ValueToJsonNode(object? value)
    {
        if (value == null) return null;
        var type = value.GetType();
        
        // Standard primitives
        if (value is float f) return JsonValue.Create(f);
        if (value is double d) return JsonValue.Create(d);
        if (value is int i) return JsonValue.Create(i);
        if (value is bool b) return JsonValue.Create(b);
        if (value is string s) return JsonValue.Create(s);
        if (type.IsEnum) return JsonValue.Create(value.ToString());

        // Complex types via Reflection (Cross-Assembly Safe)
        string typeName = type.Name;
        if (typeName.Contains("Vector2")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y") };
        if (typeName.Contains("Vector3")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y"), ["Z"] = GetReflectedValue(value, "Z") };
        if (typeName.Contains("Vector4")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y"), ["Z"] = GetReflectedValue(value, "Z"), ["W"] = GetReflectedValue(value, "W") };
        if (typeName.Contains("Color")) return new JsonObject { ["R"] = GetReflectedValue(value, "R"), ["G"] = GetReflectedValue(value, "G"), ["B"] = GetReflectedValue(value, "B"), ["A"] = GetReflectedValue(value, "A") };
        if (typeName.Contains("Sprite")) return new JsonObject { ["Path"] = NormalizePath((string?)GetReflectedValue(value, "Path")) };

        // Fallback for custom nested objects
        try { return JsonNode.Parse(JsonSerializer.Serialize(value)); } catch { return null; }
    }

    private static JsonNode? GetReflectedValue(object obj, string memberName)
    {
        var type = obj.GetType();
        var member = (MemberInfo?)type.GetProperty(memberName) ?? type.GetField(memberName);
        if (member == null) return null;
        
        object? val = member is PropertyInfo p ? p.GetValue(obj) : ((FieldInfo)member).GetValue(obj);
        if (val == null) return null;
        
        // Return as basic JsonValue
        if (val is float f) return JsonValue.Create(f);
        if (val is double d) return JsonValue.Create(d);
        if (val is int i) return JsonValue.Create(i);
        if (val is string s) return JsonValue.Create(s);
        if (val is bool b) return JsonValue.Create(b);
        return null;
    }

    public static void Deserialize(World.World world, string json, Assembly? userAssembly = null)
    {
        if (string.IsNullOrEmpty(json)) return;
        var array = JsonNode.Parse(json)?.AsArray();
        if (array == null) return;

        world.ClearAllEntities();
        var entities = new List<Entity>();

        foreach (var node in array)
        {
            if (node == null) continue;
            var entity = world.CreateEntity((string?)node["Name"] ?? "Entity");
            entity.Active = (bool?)node["Active"] ?? true;
            entity.Transform.Position = DeserializeVector2(node["Position"]);
            entity.Transform.Rotation = (float?)node["Rotation"] ?? 0f;
            entity.Transform.Scale = DeserializeVector2(node["Scale"]);
            entities.Add(entity);
        }

        for (int i = 0; i < array.Count; i++)
        {
            int pIdx = (int?)array[i]?["ParentIndex"] ?? -1;
            if (pIdx >= 0 && pIdx < entities.Count)
                entities[i].Transform.SetParent(entities[pIdx].Transform, false);
        }

        for (int i = 0; i < array.Count; i++)
        {
            var comps = array[i]?["Components"]?.AsArray();
            if (comps == null) continue;

            foreach (var compNode in comps)
            {
                if (compNode == null) continue;
                string? typeName = (string?)compNode["Type"];
                if (typeName == null) continue;

                Type? type = ResolveType(typeName, userAssembly);
                if (type == null) continue;

                var component = entities[i].AddComponent(type);
                var fields = compNode["Fields"]?.AsObject();
                if (fields != null)
                {
                    foreach (var kvp in fields)
                        ApplyJsonToMember(component, kvp.Key, kvp.Value);
                }
            }
        }
    }

    private static Type? ResolveType(string name, Assembly? userAsm)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null) return t;
        }
        return userAsm?.GetType(name);
    }

    private static void ApplyJsonToMember(object target, string name, JsonNode? node)
    {
        if (node == null) return;
        var type = target.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var member = (MemberInfo?)type.GetProperty(name, flags) ?? type.GetField(name, flags);
        if (member == null) return;

        Type t = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
        string tName = t.Name;
        object? val = null;

        try {
            if (t == typeof(float)) val = (float?)node;
            else if (t == typeof(double)) val = (double?)node;
            else if (t == typeof(int)) val = (int?)node;
            else if (t == typeof(bool)) val = (bool?)node;
            else if (t == typeof(string)) val = (string?)node;
            else if (tName.Contains("Vector2")) val = DeserializeVector2(node);
            else if (tName.Contains("Vector3")) val = new Vector3((float?)node["X"] ?? 0, (float?)node["Y"] ?? 0, (float?)node["Z"] ?? 0);
            else if (tName.Contains("Vector4")) val = new Vector4((float?)node["X"] ?? 0, (float?)node["Y"] ?? 0, (float?)node["Z"] ?? 0, (float?)node["W"] ?? 0);
            else if (tName.Contains("Color")) val = new Verity.Core.Color((float?)node["R"] ?? 1, (float?)node["G"] ?? 1, (float?)node["B"] ?? 1, (float?)node["A"] ?? 1);
            else if (tName.Contains("Sprite")) val = new Sprite((string?)node["Path"] ?? "");
            else if (t.IsEnum) val = Enum.Parse(t, node.ToString());
            else val = JsonSerializer.Deserialize(node.ToJsonString(), t, _options);
        } catch { }

        if (val != null)
        {
            if (member is PropertyInfo pi && pi.CanWrite) pi.SetValue(target, val);
            else if (member is FieldInfo fi) fi.SetValue(target, val);
        }
    }

    private static string NormalizePath(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return "";
        int idx = fullPath.IndexOf("Assets", StringComparison.OrdinalIgnoreCase);
        return (idx >= 0) ? fullPath.Substring(idx).Replace("\\", "/") : fullPath.Replace("\\", "/");
    }

    private static JsonObject SerializeVector2(Vector2 v) => new JsonObject { ["X"] = v.X, ["Y"] = v.Y };
    private static Vector2 DeserializeVector2(JsonNode? n) => new((float?)n?["X"] ?? 0, (float?)n?["Y"] ?? 0);
}
