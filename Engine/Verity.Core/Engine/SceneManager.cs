using System.Reflection;
using System.Text.Json;
using Verity.Core.Serialization;
using Verity.Core.World;

namespace Verity.Core.Engine;

public static class WorldLoader
{
    public static event Action<string>? OnWorldLoaded;

    public static void LoadWorld(string worldPath, Assembly? userAssembly = null)
    {
        if (!File.Exists(worldPath))
        {
            Debug.LogWarning($"[WorldLoader] World file not found on disk: {worldPath}. Trying to load from memory/resources if possible.");
            return;
        }

        try {
            var json = File.ReadAllText(worldPath);
            LoadWorldFromJson(json, Path.GetFileNameWithoutExtension(worldPath), userAssembly);
            OnWorldLoaded?.Invoke(worldPath);
        } catch (Exception e) {
            Debug.LogError($"[WorldLoader] Failed to load world at {worldPath}: {e.Message}");
        }
    }

    public static void LoadWorldFromJson(string json, string name, Assembly? userAssembly = null)
    {
        try {
            var world = WorldManager.CreateOrReplaceWorld(name);
            SceneSerializer.Deserialize(world, json, userAssembly);
            WorldManager.SetActiveWorld(world);
            Debug.Log($"[WorldLoader] Successfully loaded world: {name}");
        } catch (Exception e) {
            Debug.LogError($"[WorldLoader] Failed to deserialize world '{name}': {e.Message}");
        }
    }

    public static void LoadWorldByName(string name) => PendingWorldName = name;
    public static string? PendingWorldName { get; set; }
}

public class BuildSettings
{
    public List<string> Worlds { get; set; } = new();
    public int StartWorldIndex { get; set; } = 0;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static BuildSettings Load(string path)
    {
        if (!File.Exists(path)) {
            return new BuildSettings();
        }
        try {
            var json = File.ReadAllText(path);
            return LoadFromJson(json);
        } catch (Exception e) {
            Debug.LogError($"[BuildSettings] Error loading {path}: {e.Message}");
            return new BuildSettings();
        }
    }

    public static BuildSettings LoadFromJson(string json)
    {
        try {
            var settings = JsonSerializer.Deserialize<BuildSettings>(json, JsonOptions);
            return settings ?? new BuildSettings();
        } catch {
            return new BuildSettings();
        }
    }

    public void Save(string path)
    {
        try {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
            Debug.Log($"[BuildSettings] Saved settings to {path}");
        } catch (Exception e) {
            Debug.LogError($"[BuildSettings] Error saving {path}: {e.Message}");
        }
    }
}
