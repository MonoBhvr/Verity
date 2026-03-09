using System.Numerics;

namespace Verity.Core.ECS;

public sealed class Transform : Component
{
    public Vector2 Position { get; set; }
    public float Rotation { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;

    private Transform? _parent;
    private readonly List<Transform> _children = [];

        [HideInInspector]
        public Transform? Parent
        {
            get => _parent;
            set => SetParentInternal(value, preserveWorldPosition: false);
        }

    public void SetParent(Transform? newParent, bool preserveWorldPosition = true)
    {
        SetParentInternal(newParent, preserveWorldPosition);
    }

    private void SetParentInternal(Transform? newParent, bool preserveWorldPosition)
    {
        if (_parent == newParent) return;

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
        _parent?._children.Add(this);

        if (worldPos.HasValue)
        {
            if (_parent != null)
            {
                var parentWorld = _parent.GetWorldMatrix();
                Matrix4x4.Invert(parentWorld, out var parentInv);
                var localPos3 = Vector3.Transform(new Vector3(worldPos.Value, 0f), parentInv);
                Position = new Vector2(localPos3.X, localPos3.Y);
                Rotation = worldRot!.Value - _parent.WorldRotation;
            }
            else
            {
                Position = worldPos.Value;
                Rotation = worldRot!.Value;
            }
        }
    }

    public IReadOnlyList<Transform> Children => _children;

    public Matrix4x4 GetLocalMatrix()
    {
        var scale = Matrix4x4.CreateScale(new Vector3(Scale.X, Scale.Y, 1f));
        var rotation = Matrix4x4.CreateRotationZ(Rotation * MathF.PI / 180f);
        var translation = Matrix4x4.CreateTranslation(new Vector3(Position.X, Position.Y, 0f));
        return scale * rotation * translation;
    }

    public Matrix4x4 GetWorldMatrix()
    {
        var local = GetLocalMatrix();
        return _parent != null ? local * _parent.GetWorldMatrix() : local;
    }

    public Vector2 WorldPosition
    {
        get
        {
            var world = GetWorldMatrix();
            return new Vector2(world.M41, world.M42);
        }
    }

    public float WorldRotation
    {
        get
        {
            if (_parent == null) return Rotation;
            return Rotation + _parent.WorldRotation;
        }
    }
}
