using System.Numerics;
using Verity.Core.World;

namespace Verity.Core.Engine;

public class ProjectSettings
{
    public int TargetTPS { get; set; } = 60;
    public int TargetPTPS { get; set; } = 50;
    public float EditorFontSize { get; set; } = 18f;

    [System.Text.Json.Serialization.JsonConverter(typeof(Verity.Core.Serialization.ColorConverter))]
    public Verity.Core.Color EditorWorldBackgroundColor { get; set; } = new(0.15f, 0.15f, 0.15f, 1.0f);

    // Physics Settings
    [System.Text.Json.Serialization.JsonConverter(typeof(Verity.Core.Serialization.Vector2Converter))]
    public Vector2 DefaultGravity { get; set; } = new(0, -9.81f);
    public float DefaultFriction { get; set; } = 0.5f;
    public float DefaultBounciness { get; set; } = 0.0f;
    public float DefaultLinearDamping { get; set; } = 0.1f;
    public float DefaultAngularDamping { get; set; } = 0.1f;
    public float DefaultPhysicsThreshold { get; set; } = 0.01f;
    public float DefaultSleepThreshold { get; set; } = 0.01f;

    // Sprite Import Defaults
    public int DefaultSpritePixelsPerUnit { get; set; } = 32;
    public int DefaultPointFilterMaxDimension { get; set; } = 256;
    public SpriteSizingMode DefaultSpriteSizeMode { get; set; } = SpriteSizingMode.FitInsideUnit;

    // Project Definitions
    public List<string> Tags { get; set; } = new() { "Untagged", "MainCamera", "Player", "GameController" };
    public List<string> SortingLayers { get; set; } = new() { "Default" };
    public List<string> PhysicsGroups { get; set; } = new() { "Default" };
    public string DefaultUiFontPath { get; set; } = string.Empty;
    public string DefaultUiFontGuid { get; set; } = string.Empty;
    public List<UiAssetReference> UiCatalog { get; set; } = new();
    public List<UiRoleBinding> UiRoleDefaults { get; set; } = new();
    public List<string> StartupUiRoles { get; set; } = new();
    public string LastOpenedWorldAssetPath { get; set; } = string.Empty;
    public EditorDockLayoutSettings EditorDockLayout { get; set; } = new();

    public static ProjectSettings Default => new();
}

public sealed class UiAssetReference
{
    public string Name { get; set; } = string.Empty;
    public UiAsset Asset { get; set; }
}

public sealed class UiRoleBinding
{
    public string Role { get; set; } = string.Empty;
    public UiAsset Asset { get; set; }
}

public class EditorDockLayoutSettings
{
    public string Ini { get; set; } = string.Empty;
    public List<string> OpenWindowIds { get; set; } = new();
}
