using Verity.Core.ECS;

namespace Verity.Tests;

internal sealed class AnimationTestProbe : Component
{
    public float FloatValue { get; set; }
    public int IntValue { get; set; }
    public bool BoolValue { get; set; }
    public string TextValue { get; set; } = string.Empty;
    public float FieldValue = 0f;
}
