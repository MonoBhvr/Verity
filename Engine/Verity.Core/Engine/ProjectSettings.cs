namespace Verity.Core.Engine;

public class ProjectSettings
{
    public int TargetTPS { get; set; } = 60;
    public int TargetPTPS { get; set; } = 50;
    public float EditorFontSize { get; set; } = 18f;
    public Verity.Core.Color EditorWorldBackgroundColor { get; set; } = new(0.15f, 0.15f, 0.15f, 1.0f);

    // Physics Settings
    public System.Numerics.Vector2 DefaultGravity { get; set; } = new(0, -9.81f);
    public float DefaultFriction { get; set; } = 0.5f;
    public float DefaultBounciness { get; set; } = 0.0f;
    public float DefaultLinearDamping { get; set; } = 0.1f;
    public float DefaultAngularDamping { get; set; } = 0.1f;
    public float DefaultPhysicsThreshold { get; set; } = 0.01f;
    public float DefaultSleepThreshold { get; set; } = 0.01f;

    public static ProjectSettings Default => new();
}
