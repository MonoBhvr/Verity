namespace Verity.Core.ECS;

public abstract class Script : Component
{
    internal bool HasStarted;

    public virtual void Awake() { }

    public virtual void Start() { }

    public virtual void Update() { }

    public virtual void FixedUpdate() { }

    public virtual void LateUpdate() { }

    public override void OnDestroy() { }
}
