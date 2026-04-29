using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verity.Core.Audio;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Core.Serialization;

public static class SceneSerializer
{
    public static string? AssetRootPath { get; set; }

    private static bool AllowsMultipleComponents(Type type) => type == typeof(LuaScriptComponent);

    private enum SerializationMode
    {
        World,
        Blueprint,
        Entity
    }

    private static readonly HashSet<string> PostDeserializeEnableTypes = new(StringComparer.Ordinal)
    {
        "Verity.Core.ECS.Animator",
        "Verity.Core.Physics.TilemapShape",
        "Verity.Graphics.TilemapRenderer"
    };

    private static readonly JsonSerializerOptions _options = new() { 
        WriteIndented = true,
        Converters = { new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new SpriteConverter(), new StyleAssetConverter(), new ShaderAssetConverter(), new AudioClipConverter(), new ColorConverter(), new TileBaseConverter(), new TilemapTilesConverter() }
    };

    private static string AssetRoot => string.IsNullOrWhiteSpace(AssetRootPath) ? AppContext.BaseDirectory : AssetRootPath;

    public static string Serialize(World.World world)
    {
        var root = new JsonObject();
        
        var settings = new JsonObject
        {
            ["UseCustomSettings"] = world.UseCustomSettings,
            ["CustomTPS"] = world.CustomTPS,
            ["CustomPTPS"] = world.CustomPTPS,

            // Physics overrides
            ["CustomGravity"] = SerializeVector2(world.CustomGravity),
            ["CustomFriction"] = world.CustomFriction,
            ["CustomBounciness"] = world.CustomBounciness,
            ["CustomLinearDamping"] = world.CustomLinearDamping,
            ["CustomAngularDamping"] = world.CustomAngularDamping,
            ["CustomPhysicsThreshold"] = world.CustomPhysicsThreshold,
            ["UiRoleOverrides"] = SerializeUiRoleBindings(world.UiRoleOverrides)
        };
        root["WorldSettings"] = settings;

        var rootArray = new JsonArray();
        var flattened = new List<Entity>();
        foreach (var ent in world.RootEntities)
            SerializeEntityRecursive(ent, rootArray, -1, flattened, SerializationMode.World);
        
        root["Entities"] = rootArray;
        return root.ToJsonString(_options);
    }

    public static string SerializeBlueprint(World.World world)
    {
        var rootArray = new JsonArray();
        var flattened = new List<Entity>();
        foreach (var ent in world.RootEntities)
            SerializeEntityRecursive(ent, rootArray, -1, flattened, SerializationMode.Blueprint);

        return rootArray.ToJsonString(_options);
    }

    public static string SerializeEntity(Entity entity)
    {
        var rootArray = new JsonArray();
        var flattened = new List<Entity>();
        SerializeEntityRecursive(entity, rootArray, -1, flattened, SerializationMode.Entity);
        return rootArray.ToJsonString(_options);
    }

    private static void SerializeEntityRecursive(Entity entity, JsonArray array, int parentIndex, List<Entity> flattened, SerializationMode mode)
    {
        if (mode == SerializationMode.World && entity.IsBlueprintInstanceRoot)
        {
            flattened.Add(entity);
            array.Add(SerializeBlueprintInstance(entity, parentIndex));
            return;
        }

        int currentIndex = flattened.Count;
        flattened.Add(entity);

        array.Add(SerializeStandardEntity(entity, parentIndex));

        foreach (var child in entity.Transform.Children)
            SerializeEntityRecursive(child.Owner, array, currentIndex, flattened, mode);
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

    private static JsonObject SerializeStandardEntity(Entity entity, int parentIndex)
    {
        var entityJson = new JsonObject
        {
            ["Id"] = entity.Id.ToString(),
            ["Name"] = entity.Name,
            ["Tag"] = entity.Tag,
            ["Active"] = entity.Active,
            ["ParentIndex"] = parentIndex,
            ["Position"] = SerializeVector2(entity.Transform.Position),
            ["Rotation"] = entity.Transform.Rotation,
            ["Scale"] = SerializeVector2(entity.Transform.Scale)
        };

        entityJson["Components"] = CaptureComponents(entity);
        return entityJson;
    }

    private static JsonArray CaptureComponents(Entity entity)
    {
        var componentsArray = new JsonArray();
        foreach (var component in entity.GetAllComponents())
        {
            if (component is Transform)
                continue;

            var type = component.GetType();
            var fieldsJson = new JsonObject();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                if (!ShouldSerializeMember(field))
                    continue;

                JsonNode? node = ValueToJsonNode(field.GetValue(component), field, component);
                if (node != null)
                    fieldsJson[field.Name] = node;
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (property.DeclaringType == typeof(Component) || !property.CanRead || !property.CanWrite)
                    continue;
                if (!ShouldSerializeMember(property))
                    continue;

                try
                {
                    JsonNode? node = ValueToJsonNode(property.GetValue(component), property, component);
                    if (node != null)
                        fieldsJson[property.Name] = node;
                }
                catch
                {
                }
            }

            componentsArray.Add(new JsonObject
            {
                ["Type"] = type.FullName ?? type.Name,
                ["Enabled"] = component.Enabled,
                ["Fields"] = fieldsJson
            });
        }

        return componentsArray;
    }

    private static JsonObject SerializeBlueprintInstance(Entity instanceRoot, int parentIndex)
    {
        string blueprintPath = ResolveBlueprintAssetPath(instanceRoot);
        if (string.IsNullOrWhiteSpace(blueprintPath) || !File.Exists(blueprintPath))
            return SerializeStandardEntity(instanceRoot, parentIndex);

        var sourceWorld = new World.World("__blueprint_source__");
        Entity? sourceRoot = null;
        try
        {
            sourceRoot = DeserializeEntity(sourceWorld, File.ReadAllText(blueprintPath), preserveEntityIds: true);
        }
        catch
        {
            sourceRoot = null;
        }

        if (sourceRoot == null)
            return SerializeStandardEntity(instanceRoot, parentIndex);

        var sourceEntities = EnumerateDescendantsAndSelf(sourceRoot)
            .ToDictionary(entity => entity.Id, entity => entity);
        var instanceEntities = EnumerateDescendantsAndSelf(instanceRoot)
            .Where(entity => entity.BlueprintSourceEntityId.HasValue)
            .ToDictionary(entity => entity.BlueprintSourceEntityId!.Value, entity => entity);

        var overrides = new JsonArray();
        foreach (var pair in instanceEntities.OrderBy(pair => GetBlueprintDepth(pair.Value)))
        {
            if (!sourceEntities.TryGetValue(pair.Key, out var sourceEntity))
                continue;

            JsonObject? diff = BuildBlueprintOverrideNode(sourceEntity, pair.Value);
            if (diff != null)
                overrides.Add(diff);
        }

        return new JsonObject
        {
            ["Id"] = instanceRoot.Id.ToString(),
            ["Name"] = instanceRoot.Name,
            ["Active"] = instanceRoot.Active,
            ["ParentIndex"] = parentIndex,
            ["Position"] = SerializeVector2(instanceRoot.Transform.Position),
            ["Rotation"] = instanceRoot.Transform.Rotation,
            ["Scale"] = SerializeVector2(instanceRoot.Transform.Scale),
            ["BlueprintInstance"] = new JsonObject
            {
                ["Asset"] = AssetPathUtility.ToJsonNode(instanceRoot.BlueprintAssetPath, instanceRoot.BlueprintAssetGuid),
                ["RootSourceId"] = instanceRoot.BlueprintSourceEntityId?.ToString(),
                ["Overrides"] = overrides
            }
        };
    }

    private static JsonObject? BuildBlueprintOverrideNode(Entity sourceEntity, Entity instanceEntity)
    {
        var node = new JsonObject
        {
            ["SourceId"] = sourceEntity.Id.ToString(),
            ["EntityId"] = instanceEntity.Id.ToString()
        };

        bool changed = sourceEntity.Id != instanceEntity.Id;
        if (!string.Equals(sourceEntity.Name, instanceEntity.Name, StringComparison.Ordinal))
        {
            node["Name"] = instanceEntity.Name;
            changed = true;
        }

        if (sourceEntity.Active != instanceEntity.Active)
        {
            node["Active"] = instanceEntity.Active;
            changed = true;
        }

        if (sourceEntity.Transform.Position != instanceEntity.Transform.Position)
        {
            node["Position"] = SerializeVector2(instanceEntity.Transform.Position);
            changed = true;
        }

        if (Math.Abs(sourceEntity.Transform.Rotation - instanceEntity.Transform.Rotation) > 0.0001f)
        {
            node["Rotation"] = instanceEntity.Transform.Rotation;
            changed = true;
        }

        if (sourceEntity.Transform.Scale != instanceEntity.Transform.Scale)
        {
            node["Scale"] = SerializeVector2(instanceEntity.Transform.Scale);
            changed = true;
        }

        JsonArray componentDiffs = BuildComponentOverrides(sourceEntity, instanceEntity);
        if (componentDiffs.Count > 0)
        {
            node["Components"] = componentDiffs;
            changed = true;
        }

        return changed ? node : null;
    }

    private static JsonArray BuildComponentOverrides(Entity sourceEntity, Entity instanceEntity)
    {
        var overrides = new JsonArray();
        var sourceComponents = sourceEntity.GetAllComponents()
            .Where(component => component is not Transform)
            .ToDictionary(component => component.GetType().FullName ?? component.GetType().Name, component => component, StringComparer.Ordinal);
        var instanceComponents = instanceEntity.GetAllComponents()
            .Where(component => component is not Transform)
            .ToDictionary(component => component.GetType().FullName ?? component.GetType().Name, component => component, StringComparer.Ordinal);

        foreach (var pair in instanceComponents)
        {
            if (!sourceComponents.TryGetValue(pair.Key, out var sourceComponent))
            {
                overrides.Add(new JsonObject
                {
                    ["Type"] = pair.Key,
                    ["Added"] = true,
                    ["Enabled"] = pair.Value.Enabled,
                    ["Fields"] = ((JsonObject?)CaptureComponentNode(pair.Value)?["Fields"])?.DeepClone()
                });
                continue;
            }

            JsonObject fieldsDiff = BuildFieldDiff(sourceComponent, pair.Value);
            if (fieldsDiff.Count == 0 && sourceComponent.Enabled == pair.Value.Enabled)
                continue;

            var node = new JsonObject
            {
                ["Type"] = pair.Key
            };
            if (sourceComponent.Enabled != pair.Value.Enabled)
                node["Enabled"] = pair.Value.Enabled;
            if (fieldsDiff.Count > 0)
                node["Fields"] = fieldsDiff;
            overrides.Add(node);
        }

        foreach (var pair in sourceComponents)
        {
            if (!instanceComponents.ContainsKey(pair.Key))
            {
                overrides.Add(new JsonObject
                {
                    ["Type"] = pair.Key,
                    ["Removed"] = true
                });
            }
        }

        return overrides;
    }

    private static JsonObject BuildFieldDiff(Component sourceComponent, Component instanceComponent)
    {
        JsonObject sourceFields = (JsonObject?)CaptureComponentNode(sourceComponent)?["Fields"] ?? new JsonObject();
        JsonObject instanceFields = (JsonObject?)CaptureComponentNode(instanceComponent)?["Fields"] ?? new JsonObject();
        var diff = new JsonObject();

        foreach (var field in instanceFields)
        {
            JsonNode? sourceValue = sourceFields[field.Key];
            if (!JsonNodesEqual(sourceValue, field.Value))
                diff[field.Key] = field.Value?.DeepClone();
        }

        return diff;
    }

    private static JsonObject CaptureComponentNode(Component component)
    {
        var node = new JsonObject();
        var fields = new JsonObject();
        var type = component.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var field in type.GetFields(flags))
        {
            if (!ShouldSerializeMember(field))
                continue;
            JsonNode? json = ValueToJsonNode(field.GetValue(component), field, component);
            if (json != null)
                fields[field.Name] = json;
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.DeclaringType == typeof(Component) || !property.CanRead || !property.CanWrite)
                continue;
            if (!ShouldSerializeMember(property))
                continue;
            try
            {
                JsonNode? json = ValueToJsonNode(property.GetValue(component), property, component);
                if (json != null)
                    fields[property.Name] = json;
            }
            catch
            {
            }
        }

        node["Type"] = type.FullName ?? type.Name;
        node["Enabled"] = component.Enabled;
        node["Fields"] = fields;
        return node;
    }

    private static bool JsonNodesEqual(JsonNode? left, JsonNode? right)
    {
        if (left == null && right == null)
            return true;
        if (left == null || right == null)
            return false;
        return string.Equals(left.ToJsonString(_options), right.ToJsonString(_options), StringComparison.Ordinal);
    }

    private static int GetBlueprintDepth(Entity entity)
    {
        int depth = 0;
        var current = entity.Transform.Parent;
        while (current != null)
        {
            depth++;
            current = current.Parent;
        }

        return depth;
    }

    private static string ResolveBlueprintAssetPath(Entity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.BlueprintAssetGuid))
        {
            string resolvedByGuid = AssetPathUtility.ResolvePath(AssetRoot, entity.BlueprintAssetPath, entity.BlueprintAssetGuid);
            if (!string.IsNullOrWhiteSpace(resolvedByGuid) && File.Exists(resolvedByGuid))
                return resolvedByGuid;
        }

        string resolved = AssetPathUtility.ResolvePath(AssetRoot, entity.BlueprintAssetPath, entity.BlueprintAssetGuid);
        return File.Exists(resolved) ? resolved : string.Empty;
    }

    private static IEnumerable<Entity> EnumerateDescendantsAndSelf(Entity root)
    {
        yield return root;
        foreach (Transform child in root.Transform.Children)
        {
            foreach (Entity descendant in EnumerateDescendantsAndSelf(child.Owner))
                yield return descendant;
        }
    }

    private static void SetBlueprintInstanceRootIdRecursive(Entity root, Guid rootId)
    {
        foreach (Entity entity in EnumerateDescendantsAndSelf(root))
            entity.BlueprintInstanceRootId = rootId;
    }

    public static JsonArray CaptureBlueprintInstanceOverrides(Entity instanceRoot)
    {
        if (!instanceRoot.IsBlueprintInstanceRoot)
            return [];

        string blueprintPath = ResolveBlueprintAssetPath(instanceRoot);
        if (string.IsNullOrWhiteSpace(blueprintPath) || !File.Exists(blueprintPath))
            return [];

        var sourceWorld = new World.World("__blueprint_capture__");
        Entity? sourceRoot;
        try
        {
            sourceRoot = DeserializeEntity(sourceWorld, File.ReadAllText(blueprintPath), preserveEntityIds: true);
        }
        catch
        {
            return [];
        }

        return sourceRoot == null ? [] : CaptureBlueprintInstanceOverrides(sourceRoot, instanceRoot);
    }

    public static JsonArray CaptureBlueprintInstanceOverrides(Entity sourceRoot, Entity instanceRoot)
    {
        var sourceEntities = EnumerateDescendantsAndSelf(sourceRoot)
            .ToDictionary(entity => entity.Id, entity => entity);
        var instanceEntities = EnumerateDescendantsAndSelf(instanceRoot)
            .Where(entity => entity.BlueprintSourceEntityId.HasValue)
            .ToDictionary(entity => entity.BlueprintSourceEntityId!.Value, entity => entity);

        var overrides = new JsonArray();
        foreach (var pair in instanceEntities.OrderBy(pair => GetBlueprintDepth(pair.Value)))
        {
            if (!sourceEntities.TryGetValue(pair.Key, out Entity? sourceEntity))
                continue;

            JsonObject? diff = BuildBlueprintOverrideNode(sourceEntity, pair.Value);
            if (diff != null)
                overrides.Add(diff);
        }

        return overrides;
    }

    public static Entity? InstantiateBlueprintInstance(World.World world, string blueprintPath, Assembly? userAssembly = null)
    {
        if (string.IsNullOrWhiteSpace(blueprintPath) || !File.Exists(blueprintPath))
            return null;

        var tempWorld = new World.World("__blueprint_instantiate__");
        Entity? sourceRoot;
        try
        {
            sourceRoot = DeserializeEntity(tempWorld, File.ReadAllText(blueprintPath), userAssembly, preserveEntityIds: true);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneSerializer] Failed to deserialize blueprint asset '{blueprintPath}': {e}");
            return null;
        }

        return sourceRoot == null
            ? null
            : CloneBlueprintIntoWorld(sourceRoot, world, AssetPathUtility.CreateReference(blueprintPath), userAssembly);
    }

    public static Entity? RefreshBlueprintInstance(Entity instanceRoot, JsonArray overrides, Assembly? userAssembly = null)
    {
        if (!instanceRoot.IsBlueprintInstanceRoot)
            return null;

        string blueprintPath = ResolveBlueprintAssetPath(instanceRoot);
        if (string.IsNullOrWhiteSpace(blueprintPath) || !File.Exists(blueprintPath))
            return null;

        World.World? world = instanceRoot.World;
        if (world == null)
            return null;

        var tempWorld = new World.World("__blueprint_refresh__");
        Entity? sourceRoot;
        try
        {
            sourceRoot = DeserializeEntity(tempWorld, File.ReadAllText(blueprintPath), userAssembly, preserveEntityIds: true);
        }
        catch
        {
            return null;
        }

        if (sourceRoot == null)
            return null;

        AssetReferenceData assetReference = AssetPathUtility.CreateReference(blueprintPath);
        Entity? parent = instanceRoot.Transform.Parent?.Owner;
        bool preserveWorldPosition = parent != null;

        Entity? refreshedRoot = CloneBlueprintIntoWorld(sourceRoot, world, assetReference, userAssembly);
        if (refreshedRoot == null)
            return null;

        if (parent != null)
            refreshedRoot.Transform.SetParent(parent.Transform, preserveWorldPosition);

        var entityIdMap = EnumerateDescendantsAndSelf(refreshedRoot)
            .ToDictionary(entity => entity.Id, entity => entity);
        ApplyBlueprintInstanceOverrides(refreshedRoot, overrides, world, userAssembly, entityIdMap);

        world.DestroyEntity(instanceRoot);
        world.ProcessPendingDestroys();

        return refreshedRoot;
    }

    private static JsonNode? ValueToJsonNode(object? value, MemberInfo? member = null, object? owner = null)
    {
        if (value == null) return null;
        var type = value.GetType();
        
        // Standard primitives
        if (value is float f) return JsonValue.Create(f);
        if (value is double d) return JsonValue.Create(d);
        if (value is int i) return JsonValue.Create(i);
        if (value is ulong ul) return JsonValue.Create(ul);
        if (value is bool b) return JsonValue.Create(b);
        if (value is string s)
        {
            if (member != null && member.GetCustomAttribute<AssetReferenceAttribute>() != null)
            {
                string guidMemberName = member.Name.Replace("Path", "Guid", StringComparison.Ordinal);
                string guidValue = ResolveSiblingStringMember(owner, guidMemberName);
                return AssetPathUtility.ToJsonNode(s, guidValue);
            }

            return JsonValue.Create(s);
        }
        if (type.IsEnum) return JsonValue.Create(value.ToString());

        // Complex types via Reflection (Cross-Assembly Safe)
        string typeName = type.Name;
        if (typeName.Contains("Vector2")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y") };
        if (typeName.Contains("Vector3")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y"), ["Z"] = GetReflectedValue(value, "Z") };
        if (typeName.Contains("Vector4")) return new JsonObject { ["X"] = GetReflectedValue(value, "X"), ["Y"] = GetReflectedValue(value, "Y"), ["Z"] = GetReflectedValue(value, "Z"), ["W"] = GetReflectedValue(value, "W") };
        if (typeName.Contains("Color")) return new JsonObject { ["R"] = GetReflectedValue(value, "R"), ["G"] = GetReflectedValue(value, "G"), ["B"] = GetReflectedValue(value, "B"), ["A"] = GetReflectedValue(value, "A") };
        if (value is AudioClip audioClip)
        {
            return new JsonObject
            {
                ["Name"] = audioClip.Name,
                ["Path"] = AssetPathUtility.Normalize(audioClip.Path),
                ["Guid"] = string.IsNullOrWhiteSpace(audioClip.Guid) ? AssetPathUtility.TryGetGuid(audioClip.Path) : audioClip.Guid,
                ["Type"] = audioClip.Type.ToString(),
                ["DefaultVolume"] = audioClip.DefaultVolume,
                ["DefaultPitch"] = audioClip.DefaultPitch,
                ["IsLooping"] = audioClip.IsLooping
            };
        }

        if (value is Sprite sprite)
            return AssetPathUtility.ToSpriteJsonNode(sprite);

        if (value is IPathAsset asset)
            return AssetPathUtility.ToJsonNode(asset.Path, asset.Guid);

        if (value is Component component)
        {
            return new JsonObject
            {
                ["EntityId"] = component.Owner.Id.ToString(),
                ["ComponentType"] = component.GetType().FullName ?? component.GetType().Name
            };
        }

        // Fallback for custom nested objects
        try { return JsonNode.Parse(JsonSerializer.Serialize(value, _options)); } catch { return null; }
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
        if (val is ulong ul) return JsonValue.Create(ul);
        if (val is string s) return JsonValue.Create(s);
        if (val is bool b) return JsonValue.Create(b);
        return null;
    }

    private static string ResolveSiblingStringMember(object? owner, string memberName)
    {
        if (owner == null || string.IsNullOrWhiteSpace(memberName))
            return string.Empty;

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var member = (MemberInfo?)owner.GetType().GetProperty(memberName, flags) ?? owner.GetType().GetField(memberName, flags);
        if (member is PropertyInfo property && property.PropertyType == typeof(string))
            return property.GetValue(owner) as string ?? string.Empty;
        if (member is FieldInfo field && field.FieldType == typeof(string))
            return field.GetValue(owner) as string ?? string.Empty;
        return string.Empty;
    }

    private static void SetSiblingStringMember(object owner, string memberName, string value)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var member = (MemberInfo?)owner.GetType().GetProperty(memberName, flags) ?? owner.GetType().GetField(memberName, flags);
        if (member is PropertyInfo property && property.CanWrite && property.PropertyType == typeof(string))
            property.SetValue(owner, value);
        else if (member is FieldInfo field && field.FieldType == typeof(string))
            field.SetValue(owner, value);
    }

    public static void Deserialize(World.World world, string json, Assembly? userAssembly = null, bool preserveEntityIds = true)
    {
        if (string.IsNullOrEmpty(json)) return;
        var root = JsonNode.Parse(json);
        if (root == null) return;

        JsonArray? array;
        if (root is JsonArray rootArray)
        {
            array = rootArray;
        }
        else
        {
            var settings = root["WorldSettings"];
            if (settings != null)
            {
                world.UseCustomSettings = (bool?)settings["UseCustomSettings"] ?? false;
                world.CustomTPS = (int?)settings["CustomTPS"] ?? 60;
                world.CustomPTPS = (int?)settings["CustomPTPS"] ?? 50;

                // Physics overrides
                world.CustomGravity = new Vector2((float?)settings["CustomGravity"]?["X"] ?? 0, (float?)settings["CustomGravity"]?["Y"] ?? -9.81f);
                world.CustomFriction = (float?)settings["CustomFriction"] ?? 0.5f;
                world.CustomBounciness = (float?)settings["CustomBounciness"] ?? 0.0f;
                world.CustomLinearDamping = (float?)settings["CustomLinearDamping"] ?? 0.1f;
                world.CustomAngularDamping = (float?)settings["CustomAngularDamping"] ?? 0.1f;
                world.CustomPhysicsThreshold = (float?)settings["CustomPhysicsThreshold"] ?? 0.05f;
                world.UiRoleOverrides = DeserializeUiRoleBindings(settings["UiRoleOverrides"]);
            }
            array = root["Entities"]?.AsArray();
        }

        if (array == null) return;

        world.ClearAllEntities();
        var entities = DeserializeEntitiesInternal(world, array, userAssembly, preserveEntityIds);
    }

    public static Entity? DeserializeEntity(World.World world, string json, Assembly? userAssembly = null, bool preserveEntityIds = false)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var array = JsonNode.Parse(json)?.AsArray();
        if (array == null || array.Count == 0) return null;

        var entities = DeserializeEntitiesInternal(world, array, userAssembly, preserveEntityIds);
        return entities.Count > 0 ? entities[0] : null;
    }

    private static List<Entity> DeserializeEntitiesInternal(World.World world, JsonArray array, Assembly? userAssembly, bool preserveEntityIds)
    {
        var entities = new List<Entity>();
        var nodeEntities = new List<Entity?>(array.Count);
        var entityIdMap = new Dictionary<Guid, Entity>();
        var blueprintOverrideNodes = new List<(Entity Root, JsonArray Overrides)>();

        // 1. Create all root entries. Blueprint instances expand into full hierarchies here.
        foreach (JsonNode? node in array)
        {
            if (node == null)
            {
                nodeEntities.Add(null);
                continue;
            }

            JsonObject? blueprintInstance = node["BlueprintInstance"]?.AsObject();
            if (blueprintInstance != null)
            {
                Entity? instanceRoot = DeserializeBlueprintInstance(world, node, blueprintInstance, userAssembly, preserveEntityIds, entityIdMap);
                if (instanceRoot != null)
                {
                    entities.Add(instanceRoot);
                    nodeEntities.Add(instanceRoot);
                    if (blueprintInstance["Overrides"] is JsonArray overrides && overrides.Count > 0)
                        blueprintOverrideNodes.Add((instanceRoot, overrides));
                }
                else
                {
                    nodeEntities.Add(null);
                    Debug.LogError($"[SceneSerializer] Failed to instantiate blueprint entity '{(string?)node["Name"] ?? "Entity"}'.");
                }

                continue;
            }

            var entity = world.CreateEntity((string?)node["Name"] ?? "Entity");
            if (Guid.TryParse((string?)node["Id"], out var guid))
            {
                if (preserveEntityIds)
                    entity.Id = guid;
                entityIdMap[guid] = entity;
            }

            entity.Tag = (string?)node["Tag"] ?? "Untagged";
            entity.Active = (bool?)node["Active"] ?? true;
            entities.Add(entity);
            nodeEntities.Add(entity);
        }

        // 2. Restore hierarchy FIRST
        for (int i = 0; i < array.Count; i++)
        {
            Entity? entity = nodeEntities[i];
            if (entity == null || array[i]?["BlueprintInstance"] != null)
                continue;

            int pIdx = (int?)array[i]?["ParentIndex"] ?? -1;
            if (pIdx >= 0 && pIdx < nodeEntities.Count && nodeEntities[pIdx] != null)
                entity.Transform.SetParent(nodeEntities[pIdx]!.Transform, false);
        }

        // 3. Set local transforms (now they are correctly relative to restored parents)
        for (int i = 0; i < array.Count; i++)
        {
            var node = array[i];
            Entity? entity = nodeEntities[i];
            if (node == null || entity == null || node["BlueprintInstance"] != null)
                continue;
            entity.Transform.Position = DeserializeVector2(node["Position"]);
            entity.Transform.Rotation = (float?)node["Rotation"] ?? 0f;
            entity.Transform.Scale = DeserializeVector2(node["Scale"]);
        }

        // 4. Create components first so reference fields can resolve across entities
        var pendingFields = new List<(Component Component, JsonObject Fields, bool Enabled)>();
        for (int i = 0; i < array.Count; i++)
        {
            Entity? entity = nodeEntities[i];
            if (entity == null || array[i]?["BlueprintInstance"] != null)
                continue;

            var comps = array[i]?["Components"]?.AsArray();
            if (comps == null) continue;

            foreach (var compNode in comps)
            {
                if (compNode == null) continue;
                string? typeName = (string?)compNode["Type"];
                if (typeName == null) continue;

                Type? type = ResolveType(typeName, userAssembly);
                if (type == null) continue;

                var component = AllowsMultipleComponents(type) ? null : entity.GetComponent(type);
                if (component == null)
                {
                    if (!entity.CanAddComponent(type, out var reason))
                    {
                        Debug.LogWarning($"[SceneSerializer] Skipping component '{type.Name}' on '{entity.Name}': {reason}");
                        continue;
                    }

                    component = entity.AddComponent(type);
                }

                var fields = compNode["Fields"]?.AsObject();
                if (fields != null)
                    pendingFields.Add((component, fields, (bool?)compNode["Enabled"] ?? true));
                else
                    component.Enabled = (bool?)compNode["Enabled"] ?? true;
            }
        }

        foreach (var (root, overrides) in blueprintOverrideNodes)
            ApplyBlueprintInstanceOverrides(root, overrides, world, userAssembly, entityIdMap);

        // 5. Apply serialized values after all components exist
        foreach (var (component, fields, enabled) in pendingFields)
        {
            foreach (var kvp in fields)
                ApplyJsonToMember(component, kvp.Key, kvp.Value, world, userAssembly, entityIdMap);

            component.Enabled = enabled;
        }

        foreach (var entity in entities)
        {
            foreach (var component in entity.GetAllComponents())
            {
                if (component.GetType().FullName is string typeName &&
                    PostDeserializeEnableTypes.Contains(typeName))
                {
                    component.InitializeAfterDeserialization();
                }
            }
        }

        if (preserveEntityIds)
        {
            IEnumerable<Entity> hierarchyRoots = entities.Where(entity => entity.Transform.Parent == null);
            IEnumerable<Entity> allLoadedEntities = hierarchyRoots.SelectMany(EnumerateDescendantsAndSelf);

            EnsureBlueprintInstanceEntityIds(allLoadedEntities);
            EnsureUniqueEntityIds(allLoadedEntities);
        }
        
        return entities;
    }

    private static Entity? DeserializeBlueprintInstance(
        World.World world,
        JsonNode node,
        JsonObject blueprintInstance,
        Assembly? userAssembly,
        bool preserveEntityIds,
        Dictionary<Guid, Entity> entityIdMap)
    {
        AssetReferenceData assetReference = AssetPathUtility.FromJsonNode(blueprintInstance["Asset"]);
        string blueprintPath = AssetPathUtility.ResolvePath(AssetRoot, assetReference.Path, assetReference.Guid);
        if (!File.Exists(blueprintPath))
        {
            Debug.LogError($"[SceneSerializer] Blueprint asset not found: {blueprintPath}");
            return null;
        }

        var tempWorld = new World.World("__blueprint_instance__");
        Entity? sourceRoot;
        try
        {
            sourceRoot = DeserializeEntity(tempWorld, File.ReadAllText(blueprintPath), userAssembly, preserveEntityIds: true);
        }
        catch
        {
            return null;
        }

        if (sourceRoot == null)
        {
            Debug.LogError($"[SceneSerializer] Blueprint asset produced no root entity: {blueprintPath}");
            return null;
        }

        Entity? instanceRoot = CloneBlueprintIntoWorld(sourceRoot, world, assetReference, userAssembly);
        if (instanceRoot == null)
        {
            Debug.LogError($"[SceneSerializer] Failed to clone blueprint into world: {blueprintPath}");
            return null;
        }

        if (Guid.TryParse((string?)node["Id"], out Guid rootId))
        {
            if (preserveEntityIds)
                instanceRoot.Id = rootId;
            entityIdMap[rootId] = instanceRoot;
        }

        SetBlueprintInstanceRootIdRecursive(instanceRoot, instanceRoot.Id);

        instanceRoot.Name = (string?)node["Name"] ?? instanceRoot.Name;
        instanceRoot.Active = (bool?)node["Active"] ?? instanceRoot.Active;
        instanceRoot.Transform.Position = DeserializeVector2(node["Position"]);
        instanceRoot.Transform.Rotation = (float?)node["Rotation"] ?? instanceRoot.Transform.Rotation;
        instanceRoot.Transform.Scale = DeserializeVector2(node["Scale"]);

        foreach (Entity entity in EnumerateDescendantsAndSelf(instanceRoot))
            entityIdMap[entity.Id] = entity;

        return instanceRoot;
    }

    private static Entity? CloneBlueprintIntoWorld(Entity sourceRoot, World.World targetWorld, AssetReferenceData assetReference, Assembly? userAssembly)
    {
        Guid instanceRootId = Guid.Empty;
        Entity? clonedRoot = null;

        void CloneRecursive(Entity sourceEntity, Entity? parent)
        {
            Entity clone = targetWorld.CreateEntity(sourceEntity.Name);
            if (clonedRoot == null)
            {
                clonedRoot = clone;
                instanceRootId = clone.Id;
            }

            clone.Active = sourceEntity.Active;
            clone.Transform.Position = sourceEntity.Transform.Position;
            clone.Transform.Rotation = sourceEntity.Transform.Rotation;
            clone.Transform.Scale = sourceEntity.Transform.Scale;
            clone.BlueprintAssetPath = assetReference.Path;
            clone.BlueprintAssetGuid = assetReference.Guid;
            clone.BlueprintSourceEntityId = sourceEntity.Id;
            clone.BlueprintInstanceRootId = instanceRootId;

            if (parent != null)
                clone.Transform.SetParent(parent.Transform, false);

            foreach (JsonNode? componentNode in CaptureComponents(sourceEntity))
            {
                string? typeName = (string?)componentNode?["Type"];
                if (typeName == null)
                    continue;

                Type? type = ResolveType(typeName, userAssembly);
                if (type == null)
                    continue;
                if (!AllowsMultipleComponents(type) && clone.GetComponent(type) != null)
                    continue;
                if (!clone.CanAddComponent(type, out _))
                    continue;

                Component component = clone.AddComponent(type);
                if (componentNode?["Fields"] is JsonObject fields)
                {
                    foreach (var field in fields)
                        ApplyJsonToMember(component, field.Key, field.Value, targetWorld, userAssembly);
                }

                component.Enabled = (bool?)componentNode?["Enabled"] ?? true;
            }

            foreach (Transform child in sourceEntity.Transform.Children)
                CloneRecursive(child.Owner, clone);
        }

        CloneRecursive(sourceRoot, null);
        return clonedRoot;
    }

    private static void ApplyBlueprintInstanceOverrides(
        Entity root,
        JsonArray overrides,
        World.World world,
        Assembly? userAssembly,
        Dictionary<Guid, Entity> entityIdMap)
    {
        var sourceMap = EnumerateDescendantsAndSelf(root)
            .Where(entity => entity.BlueprintSourceEntityId.HasValue)
            .ToDictionary(entity => entity.BlueprintSourceEntityId!.Value, entity => entity);

        foreach (JsonNode? node in overrides)
        {
            if (node == null || !Guid.TryParse((string?)node["SourceId"], out Guid sourceId))
                continue;
            if (!sourceMap.TryGetValue(sourceId, out Entity? entity))
                continue;

            if (Guid.TryParse((string?)node["EntityId"], out Guid entityId))
            {
                entity.Id = entityId;
                entityIdMap[entity.Id] = entity;
                if (entity == root)
                    SetBlueprintInstanceRootIdRecursive(root, entity.Id);
            }

            if (node["Name"] != null)
                entity.Name = (string?)node["Name"] ?? entity.Name;
            if (node["Active"] != null)
                entity.Active = (bool?)node["Active"] ?? entity.Active;
            if (node["Position"] != null)
                entity.Transform.Position = DeserializeVector2(node["Position"]);
            if (node["Rotation"] != null)
                entity.Transform.Rotation = (float?)node["Rotation"] ?? entity.Transform.Rotation;
            if (node["Scale"] != null)
                entity.Transform.Scale = DeserializeVector2(node["Scale"]);

            if (node["Components"] is not JsonArray componentOverrides)
                continue;

            foreach (JsonNode? componentNode in componentOverrides)
            {
                string? typeName = (string?)componentNode?["Type"];
                if (typeName == null)
                    continue;

                Type? type = ResolveType(typeName, userAssembly);
                if (type == null)
                    continue;

                if ((bool?)componentNode?["Removed"] == true)
                {
                    if (entity.GetComponent(type) is Component existingComponent)
                        entity.RemoveComponent(existingComponent);
                    continue;
                }

                Component? component = AllowsMultipleComponents(type) ? null : entity.GetComponent(type);
                if ((bool?)componentNode?["Added"] == true && component == null)
                {
                    if (!entity.CanAddComponent(type, out _))
                        continue;
                    component = entity.AddComponent(type);
                }

                if (component == null)
                    continue;

                if (componentNode?["Fields"] is JsonObject fields)
                {
                    foreach (var field in fields)
                        ApplyJsonToMember(component, field.Key, field.Value, world, userAssembly, entityIdMap);
                }

                if (componentNode?["Enabled"] != null)
                    component.Enabled = (bool?)componentNode["Enabled"] ?? component.Enabled;
            }
        }
    }

    private static void EnsureUniqueEntityIds(IEnumerable<Entity> entities)
    {
        var seen = new HashSet<Guid>();
        foreach (var entity in entities)
        {
            if (seen.Add(entity.Id))
                continue;

            Guid oldId = entity.Id;
            entity.Id = Guid.NewGuid();
            Debug.LogWarning($"[SceneSerializer] Duplicate entity id detected during load. Reassigned '{entity.Name}' from {oldId} to {entity.Id}.");
            seen.Add(entity.Id);
        }
    }

    private static void EnsureBlueprintInstanceEntityIds(IEnumerable<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (!entity.IsBlueprintInstance || entity.IsBlueprintInstanceRoot)
                continue;

            // Child blueprint instances must keep their own persistent ids.
            // If they fall back to the blueprint source id, separate instances of
            // the same blueprint will collide when loaded into one world.
            if (entity.BlueprintSourceEntityId.HasValue && entity.Id == entity.BlueprintSourceEntityId.Value)
                entity.Id = Guid.NewGuid();
        }
    }

    private static Type? ResolveType(string name, Assembly? userAsm)
    {
        // List of core engine namespaces to search in AppDomain
        string[] engineNamespaces = { "Verity.Core", "Verity.Graphics", "Verity.Input" };
        string shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        bool looksLikeUserScript = !engineNamespaces.Any(ns => name.StartsWith(ns));

        // 1. For user scripts, prefer the freshly compiled user assembly first.
        if (looksLikeUserScript && userAsm != null)
        {
            var t = userAsm.GetType(name);
            if (t != null) return t;

            foreach (var type in userAsm.GetTypes())
            {
                if (type.Name == shortName || type.FullName == name) return type;
            }
        }

        // 2. Prefer built-in gameplay/runtime types already loaded in AppDomain.
        // This avoids stale UserScripts.dll content overriding browser-safe built-in scripts.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var exact = asm.GetType(name);
                if (exact != null) return exact;

                if (string.Equals(asm.GetName().Name, "Verity.Game", StringComparison.Ordinal))
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == shortName || type.FullName == name)
                            return type;
                    }
                }
            }
            catch { }
        }

        // 3. Search in AppDomain (remaining loaded types)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try {
                var t = asm.GetType(name);
                if (t != null) return t;
            } catch { }
        }

        // 4. Last resort: Global search by short name (only if userAsm didn't yield anything)
        if (userAsm == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == shortName) return type;
                    }
                } catch { }
            }
        }

        return null;
    }

    private static void ApplyJsonToMember(object target, string name, JsonNode? node, World.World world, Assembly? userAssembly, IReadOnlyDictionary<Guid, Entity>? entityIdMap = null)
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
            else if (t == typeof(ulong)) val = (ulong?)node;
            else if (t == typeof(bool)) val = (bool?)node;
            else if (t == typeof(string))
            {
                if (member.GetCustomAttribute<AssetReferenceAttribute>() != null)
                {
                    var reference = AssetPathUtility.FromJsonNode(node);
                    val = reference.Path;
                    SetSiblingStringMember(target, name.Replace("Path", "Guid", StringComparison.Ordinal), reference.Guid);
                }
                else
                {
                    val = node is JsonValue ? (string?)node : node.ToString();
                }
            }
            else if (tName.Contains("Vector2")) val = DeserializeVector2(node);
            else if (tName.Contains("Vector3")) val = new Vector3((float?)node["X"] ?? 0, (float?)node["Y"] ?? 0, (float?)node["Z"] ?? 0);
            else if (tName.Contains("Vector4")) val = new Vector4((float?)node["X"] ?? 0, (float?)node["Y"] ?? 0, (float?)node["Z"] ?? 0, (float?)node["W"] ?? 0);
            else if (tName.Contains("Color")) val = new Verity.Core.Color((float?)node["R"] ?? 1, (float?)node["G"] ?? 1, (float?)node["B"] ?? 1, (float?)node["A"] ?? 1);
            else if (t == typeof(AudioClip))
            {
                var reference = AssetPathUtility.FromJsonNode(node);
                string path = reference.Path;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AudioType? clipType = Enum.TryParse<AudioType>((string?)node["Type"], true, out var parsedType) ? parsedType : null;
                    var clip = new AudioClip { Path = path, Guid = reference.Guid, Type = clipType ?? AudioClip.GuessType(path) };
                    clip.Name = (string?)node["Name"] ?? clip.Name;
                    clip.DefaultVolume = (float?)node["DefaultVolume"] ?? clip.DefaultVolume;
                    clip.DefaultPitch = (float?)node["DefaultPitch"] ?? clip.DefaultPitch;
                    clip.IsLooping = (bool?)node["IsLooping"] ?? clip.IsLooping;
                    val = clip;
                }
            }
            else if (t == typeof(Sprite)) { val = AssetPathUtility.FromSpriteJsonNode(node); }
            else if (t == typeof(StyleAsset)) { var reference = AssetPathUtility.FromJsonNode(node); val = new StyleAsset(reference.Path, reference.Guid); }
            else if (t == typeof(ShaderAsset)) { var reference = AssetPathUtility.FromJsonNode(node); val = new ShaderAsset(reference.Path, reference.Guid); }
            else if (t == typeof(Dictionary<(int x, int y), TileBase>))
            {
                val = JsonSerializer.Deserialize<Dictionary<(int x, int y), TileBase>>(node.ToJsonString(), _options);
            }
            else if (typeof(Component).IsAssignableFrom(t))
            {
                if (Guid.TryParse((string?)node["EntityId"], out var entityId))
                {
                    Entity? entity = null;
                    if (entityIdMap != null)
                        entityIdMap.TryGetValue(entityId, out entity);
                    entity ??= world.GetAllEntities().FirstOrDefault(e => e.Id == entityId);
                    Type? componentType = ResolveType((string?)node["ComponentType"] ?? t.FullName ?? t.Name, userAssembly) ?? t;
                    val = entity?.GetComponent(componentType);
                }
            }
            else if (t.IsEnum) val = Enum.Parse(t, node.ToString());
            else val = JsonSerializer.Deserialize(node.ToJsonString(), t, _options);
        }
        catch (Exception ex)
        {
            string targetType = target.GetType().FullName ?? target.GetType().Name;
            string memberType = t.FullName ?? t.Name;
            Debug.LogError($"[SceneSerializer] Failed to apply member '{name}' ({memberType}) on '{targetType}': {ex}");
        }

        if (val != null)
        {
            if (member is PropertyInfo pi && pi.CanWrite) pi.SetValue(target, val);
            else if (member is FieldInfo fi) fi.SetValue(target, val);
        }
    }

    private static JsonObject SerializeVector2(Vector2 v) => new JsonObject { ["X"] = v.X, ["Y"] = v.Y };
    private static Vector2 DeserializeVector2(JsonNode? n) => new((float?)n?["X"] ?? 0, (float?)n?["Y"] ?? 0);

    private static JsonArray SerializeUiRoleBindings(IEnumerable<UiRoleBinding> bindings)
    {
        var array = new JsonArray();
        foreach (UiRoleBinding binding in bindings)
        {
            array.Add(new JsonObject
            {
                ["Role"] = binding.Role,
                ["Asset"] = AssetPathUtility.ToJsonNode(binding.Asset.Path, binding.Asset.Guid)
            });
        }

        return array;
    }

    private static List<UiRoleBinding> DeserializeUiRoleBindings(JsonNode? node)
    {
        var bindings = new List<UiRoleBinding>();
        if (node is not JsonArray array)
            return bindings;

        foreach (JsonNode? item in array)
        {
            if (item == null)
                continue;

            bindings.Add(new UiRoleBinding
            {
                Role = (string?)item["Role"] ?? string.Empty,
                Asset = item["Asset"] != null
                    ? new UiAsset(AssetPathUtility.FromJsonNode(item["Asset"]).Path, AssetPathUtility.FromJsonNode(item["Asset"]).Guid)
                    : new UiAsset((string?)item["Path"] ?? string.Empty, (string?)item["Guid"] ?? string.Empty)
            });
        }

        return bindings;
    }
}
