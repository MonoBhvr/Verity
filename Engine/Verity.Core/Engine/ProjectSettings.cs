namespace Verity.Core.Engine;

public class ProjectSettings
{
    public int TargetTPS { get; set; } = 60;
    public int TargetPTPS { get; set; } = 50;
    public float EditorFontSize { get; set; } = 18f;

    public static ProjectSettings Default => new();
}
