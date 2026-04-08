using System.Numerics;
namespace Verity.Core.ECS;

[Verity.Core.NonDisableable]
public sealed class Transform : Component
{
    private Vector2 _position;
    private float _rotation;
    private Vector2 _scale = Vector2.One;
    private Matrix4x4 _localMatrixCache;
    private Matrix4x4 _worldMatrixCache;
    private Vector2 _worldScaleCache = Vector2.One;
    private float _worldRotationCache;
    private bool _localMatrixDirty = true;
    private bool _worldMatrixDirty = true;
    private bool _worldScaleDirty = true;
    private bool _worldRotationDirty = true;

    public Vector2 Position
    {
        get => _position;
        set
        {
            if (_position == value) return;
            _position = value;
            InvalidateLocalCache();
        }
    }
    public float Rotation 
    { 
        get => _rotation; 
        set
        {
            float normalized = value % 360f;
            if (_rotation == normalized) return;
            _rotation = normalized;
            InvalidateLocalCache();
        }
    }
    public Vector2 Scale
    {
        get => _scale;
        set
        {
            if (_scale == value) return;
            _scale = value;
            InvalidateLocalCache();
        }
    }

    private Transform? _parent;
    private readonly List<Transform> _children = [];

    [HideInInspector]
    public Transform? Parent
    {
        get => _parent;
        set => SetParentInternal(value, preserveWorldPosition: true);
    }

    public void SetParent(Transform? newParent, bool preserveWorldPosition = true)
    {
        SetParentInternal(newParent, preserveWorldPosition);
    }

    public void SetParent(Transform? newParent, bool preserveWorldPosition, int siblingIndex)
    {
        SetParentInternal(newParent, preserveWorldPosition, siblingIndex);
    }

    private void SetParentInternal(Transform? newParent, bool preserveWorldPosition)
    {
        SetParentInternal(newParent, preserveWorldPosition, null);
    }

    private void SetParentInternal(Transform? newParent, bool preserveWorldPosition, int? siblingIndex)
    {
        if (_parent == newParent)
        {
            if (siblingIndex.HasValue)
                SetSiblingIndex(siblingIndex.Value);
            return;
        }

        // Cycle detection
        if (newParent != null)
        {
            var current = newParent;
            while (current != null)
            {
                if (current == this)
                {
                    Verity.Core.Debug.LogError($"[Transform] Cannot set parent to a descendant. Cycle detected!");
                    return;
                }
                current = current.Parent;
            }
        }

        var worldPos = preserveWorldPosition ? WorldPosition : (Vector2?)null;
        var worldRot = preserveWorldPosition ? WorldRotation : (float?)null;
        var worldScale = preserveWorldPosition ? WorldScale : (Vector2?)null;

        _parent?._children.Remove(this);
        
        // If we HAD a parent and now we don't, we become a root entity
        if (_parent != null && newParent == null)
        {
            Owner.World?.AddToRoot(Owner);
        }
        // If we didn't have a parent and now we DO, we are no longer a root entity
        else if (_parent == null && newParent != null)
        {
            Owner.World?.RemoveFromRoot(Owner);
        }

        _parent = newParent;
        if (_parent != null)
        {
            int insertIndex = siblingIndex.HasValue
                ? Math.Clamp(siblingIndex.Value, 0, _parent._children.Count)
                : _parent._children.Count;
            _parent._children.Insert(insertIndex, this);
        }

        InvalidateWorldCacheRecursive();

        if (worldPos.HasValue)
        {
            WorldPosition = worldPos.Value;
            WorldRotation = worldRot!.Value;
            WorldScale = worldScale!.Value;
        }

        Owner.World?.MarkHierarchyChanged();
    }

    public int GetSiblingIndex()
    {
        if (_parent == null)
            return Owner.World?.IndexOfRoot(Owner) ?? -1;

        return _parent._children.IndexOf(this);
    }

    public void SetSiblingIndex(int siblingIndex)
    {
        if (_parent == null)
        {
            Owner.World?.SetRootIndex(Owner, siblingIndex);
            return;
        }

        int currentIndex = _parent._children.IndexOf(this);
        if (currentIndex < 0)
            return;

        int clampedIndex = Math.Clamp(siblingIndex, 0, _parent._children.Count - 1);
        if (currentIndex == clampedIndex)
            return;

        _parent._children.RemoveAt(currentIndex);
        _parent._children.Insert(clampedIndex, this);
        Owner.World?.MarkHierarchyChanged();
    }

    public IReadOnlyList<Transform> Children => _children;

    public Matrix4x4 GetLocalMatrix()
    {
        if (_localMatrixDirty)
        {
            var scale = Matrix4x4.CreateScale(new Vector3(_scale.X, _scale.Y, 1f));
            var rotation = Matrix4x4.CreateRotationZ(_rotation * MathF.PI / 180f);
            var translation = Matrix4x4.CreateTranslation(new Vector3(_position.X, _position.Y, 0f));
            _localMatrixCache = scale * rotation * translation;
            _localMatrixDirty = false;
        }

        return _localMatrixCache;
    }

    public Matrix4x4 GetWorldMatrix()
    {
        if (_worldMatrixDirty)
        {
            var local = GetLocalMatrix();
            _worldMatrixCache = _parent == null ? local : local * _parent.GetWorldMatrix();
            _worldMatrixDirty = false;
        }

        return _worldMatrixCache;
    }

    public Vector2 WorldPosition
    {
        get
        {
            var m = GetWorldMatrix();
            return new Vector2(m.M41, m.M42);
        }
        set
        {
            if (_parent == null) Position = value;
            else
            {
                Matrix4x4.Invert(_parent.GetWorldMatrix(), out var invParent);
                var localPos3 = Vector3.Transform(new Vector3(value, 0f), invParent);
                Position = new Vector2(localPos3.X, localPos3.Y);
            }
        }
    }

    public float WorldRotation
    {
        get
        {
            if (_parent == null) return Rotation;
            if (_worldRotationDirty)
            {
                _worldRotationCache = Rotation + _parent.WorldRotation;
                _worldRotationDirty = false;
            }

            return _worldRotationCache;
        }
        set
        {
            if (_parent == null) Rotation = value;
            else Rotation = value - _parent.WorldRotation;
        }
    }

    public Vector2 WorldScale
    {
        get
        {
            if (_parent == null) return Scale;
            if (_worldScaleDirty)
            {
                _worldScaleCache = Scale * _parent.WorldScale;
                _worldScaleDirty = false;
            }

            return _worldScaleCache;
        }
        set
        {
            if (_parent == null) Scale = value;
            else
            {
                var pScale = _parent.WorldScale;
                Scale = new Vector2(
                    MathF.Abs(pScale.X) > 0.0001f ? value.X / pScale.X : value.X,
                    MathF.Abs(pScale.Y) > 0.0001f ? value.Y / pScale.Y : value.Y
                );
            }
        }
    }

    private void InvalidateLocalCache()
    {
        _localMatrixDirty = true;
        InvalidateWorldCacheRecursive();
    }

    private void InvalidateWorldCacheRecursive()
    {
        _worldMatrixDirty = true;
        _worldScaleDirty = true;
        _worldRotationDirty = true;

        foreach (var child in _children)
            child.InvalidateWorldCacheRecursive();
    }
}
