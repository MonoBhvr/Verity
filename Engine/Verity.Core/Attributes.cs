using System;

namespace Verity.Core;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RequireComponentAttribute : Attribute
{
    public Type RequiredType { get; }
    public RequireComponentAttribute(Type requiredType) => RequiredType = requiredType;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SerializeFieldAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class HideInInspectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class AssetReferenceAttribute : Attribute
{
    public string Extension { get; }
    public AssetReferenceAttribute(string extension = "") { Extension = extension; }
}

[AttributeUsage(AttributeTargets.Class)]
public class SingleInstancePerWorldAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class NonDisableableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class ButtonAttribute : Attribute
{
    public string? Label { get; }
    public bool Undoable { get; }
    public ButtonAttribute(string? label = null, bool undoable = false)
    {
        Label = label;
        Undoable = undoable;
    }
}

// 용도별 선택기 어트리뷰트
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class TagSelectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class PhysicsGroupSelectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class PhysicsGroupMaskSelectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SortingLayerSelectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SortingLayerMaskSelectorAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class FilterSelectorAttribute : Attribute { }

// 필터 시스템에서 사용할 타입 마커
public sealed class Tag { }
public sealed class SortingLayer { }
public sealed class PhysicsGroup { }
