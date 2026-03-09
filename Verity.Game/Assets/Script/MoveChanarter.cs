using Verity.Core.ECS;
using Verity.Input;
using System.Numerics;

public class MoveCharacter : Script
{
    public Transform pos;
    public float speed = 5f;
    private float test;
    public override void Start()
    {
         
    }

    public override void Update()
    {
        if (Input.GetKey(KeyCode.W))
            Owner.Transform.Position += new Vector2(0f, 0.01f) * speed;
        
        if (Input.GetKey(KeyCode.A))
            Owner.Transform.Position += new Vector2(-0.01f, 0f) * speed;

        if(Input.GetKey(KeyCode.D))
            Owner.Transform.Position += new Vector2(0.01f, 0) * speed;
        if (Input.GetKey(KeyCode.S))
            Owner.Transform.Position += new Vector2(0f, -0.01f) * speed;

    }
}