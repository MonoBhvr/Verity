using System.Reflection;
using System.Text.Json;
using Verity.Core.Serialization;
using Verity.Core.UI;
using Verity.Core.World;

namespace Verity.Core.Engine;

public static class WorldLoader
{
    public static event Action<string>? OnWorldLoaded;
    public static string? LastLoadError { get; private set; }

    public static void LoadWorld(string worldPath, Assembly? userAssembly = null)
    {
        if (!File.Exists(worldPath))
        {
            LastLoadError = $"World file not found: {worldPath}";
            Debug.LogWarning($"[WorldLoader] World file not found on disk: {worldPath}. Trying to load from memory/resources if possible.");
            return;
        }

        try {
            LastLoadError = null;
            var json = File.ReadAllText(worldPath);
            LoadWorldFromJson(json, Path.GetFileNameWithoutExtension(worldPath), userAssembly);
            OnWorldLoaded?.Invoke(worldPath);
        } catch (Exception e) {
            LastLoadError = e.Message;
            Debug.LogError($"[WorldLoader] Failed to load world at {worldPath}: {e}");
        }
    }

    public static void LoadWorldFromJson(string json, string name, Assembly? userAssembly = null)
    {
        try {
            LastLoadError = null;
            EventBus.Clear();
            var world = WorldManager.CreateOrReplaceWorld(name);
            UiSystem.Clear();
            SceneSerializer.Deserialize(world, json, userAssembly);
            WorldManager.SetActiveWorld(world);
            Debug.Log($"[WorldLoader] Successfully loaded world: {name}");
        } catch (Exception e) {
            LastLoadError = e.ToString();
            Debug.LogError($"[WorldLoader] Failed to deserialize world '{name}': {e}");
        }
    }

    public static void LoadWorldByName(string name) => PendingWorldName = name;
    public static string? PendingWorldName { get; set; }
}

public class BuildSettings
{
    public List<string> Worlds { get; set; } = new();
    public int StartWorldIndex { get; set; } = 0;
    public string? LogoPath { get; set; }
    public string AppName { get; set; } = "Verity Game";
    public string AppIconPath { get; set; } = string.Empty;
    public string AppIconGuid { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool WindowResizable { get; set; } = true;

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
            var settings = JsonSerializer.Deserialize(json, CoreJsonContext.Default.BuildSettings);
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
