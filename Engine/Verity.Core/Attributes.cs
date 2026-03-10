namespace Verity.Core;

[AttributeUsage(AttributeTargets.Method)]
public class ButtonAttribute : Attribute
{
    public string? Label { get; }
    public ButtonAttribute(string? label = null) => Label = label;
}
