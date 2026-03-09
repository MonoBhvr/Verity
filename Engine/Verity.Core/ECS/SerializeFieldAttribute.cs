namespace Verity.Core.ECS;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SerializeFieldAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetReferenceAttribute : Attribute
{
    public string Extension { get; }
    public AssetReferenceAttribute(string extension = "") { Extension = extension; }
}
