using Verity.Core;
using Verity.Core.ECS;
using Verity.Input;
using System.Numerics;
using Verity.Graphics;

public class ChangeColor : Script
{
    public Color color;
    public bool changed; 
    public override void Start() { }

    public override void Update() 
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            changed = !changed;
        }
        Owner.GetComponent<SpriteRenderer>().Color = changed ? color : new Vector4(1, 1, 1, 1); 
    }
}   