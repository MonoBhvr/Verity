using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Physics;
using Verity.Graphics;
using Verity.Input;

public class MoveCharacter : Script
{
    public float speed = 5f;
    public float jumpForce = 10f;
    private Physical? ps;
    private SpriteRenderer? sr;
    private int debugFrames;

    public override void Start()
    {
        ps = Owner.GetComponent<Physical>();
        sr = Owner.GetComponent<SpriteRenderer>();
        if (ps != null)
            ps.Friction = 0f;
        Debug.Log($"[MoveCharacter.Start] entity={Owner.Name}, physical={(ps != null)}, sprite={(sr != null)}");
    }

    public override void Update()
    {
        if (ps == null)
        {
            if (debugFrames < 5)
            {
                Debug.Log("[MoveCharacter.Update] skipped: no Physical");
                debugFrames++;
            }
            return;
        }

        bool left = Input.Down(KeyCode.A) || Input.Down(KeyCode.LeftArrow);
        bool right = Input.Down(KeyCode.D) || Input.Down(KeyCode.RightArrow);
        float inpX = (left ? -1 : 0) + (right ? 1 : 0);
        inpX *= speed;

        ps.Velocity = new Vector2(inpX, ps.Velocity.Y);

        if (sr != null && inpX != 0)
            sr.FlipX = inpX < 0;

        PhysicsMath.RaycastHit hit = PhysicsManager.Raycast(Owner.Transform.Position, new Vector2(0, -1), 0.55f, Owner);
        bool jump = Input.Pressed(KeyCode.W) || Input.Pressed(KeyCode.Space) || Input.Pressed(KeyCode.UpArrow);
        if (jump && hit.IsHit)
            ps.Velocity = new Vector2(ps.Velocity.X, jumpForce);

        if (debugFrames < 10)
        {
            Debug.Log($"[MoveCharacter.Update] left={left}, right={right}, jump={jump}, vel=({ps.Velocity.X:0.###},{ps.Velocity.Y:0.###}), pos=({Owner.Transform.Position.X:0.###},{Owner.Transform.Position.Y:0.###}), grounded={hit.IsHit}");
            debugFrames++;
        }
    }
}
