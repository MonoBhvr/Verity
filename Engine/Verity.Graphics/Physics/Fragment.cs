using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Graphics;

namespace Verity.Core.Physics;

public class Fragment : Script
{
    public float FadeOutDelay { get; set; } = 2.0f;
    public float FadeOutDuration { get; set; } = 1.0f;

    private float _timer = 0;
    private PolygonRenderer? _renderer;
    private Verity.Core.Color _initialColor;

    public override void Start()
    {
        _renderer = Owner.GetComponent<PolygonRenderer>();
        if (_renderer != null)
        {
            _initialColor = _renderer.Color;
        }
    }

    public override void Update()
    {
        _timer += Time.DeltaTime;

        if (_timer > FadeOutDelay)
        {
            float t = (_timer - FadeOutDelay) / FadeOutDuration;
            if (t >= 1.0f)
            {
                Entity.Destroy(Owner);
                return;
            }

            if (_renderer != null)
            {
                var color = _initialColor;
                color.A = (byte)(_initialColor.A * (1.0f - t));
                _renderer.Color = color;
            }
        }
    }
}
