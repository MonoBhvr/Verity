using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;

namespace Verity.Core.World;

/// <summary>
/// 여러 프레임을 순환하며 보여주는 애니메이션 타일
/// </summary>
public class AnimatedTile : TileBase
{
    public List<Sprite> Sprites { get; set; } = new();
    public float AnimationSpeed { get; set; } = 1.0f;
    public float StartOffset { get; set; } = 0.0f;

    public override Sprite? GetSprite(int x, int y, Tilemap tilemap)
    {
        if (Sprites.Count == 0) return null;
        
        float time = Time.TotalTime + StartOffset;
        int index = (int)MathF.Floor(time * AnimationSpeed) % Sprites.Count;
        if (index < 0) index += Sprites.Count;
        return Sprites[index];
    }
}

/// <summary>
/// 주변 8칸 타일 여부에 따라 스프라이트를 결정하는 스마트 타일
/// </summary>
public class RuleTile : TileBase
{
    public enum Neighbor { Any, Required, NotRequired }

    public class Rule
    {
        // 0 1 2
        // 3 X 4
        // 5 6 7  (인접 8칸 위치)
        public Neighbor[] Neighbors { get; set; } = new Neighbor[8];
        public Sprite? Sprite { get; set; }
    }

    public Sprite? DefaultSprite { get; set; }
    public List<Rule> Rules { get; set; } = new();

    public override Sprite? GetSprite(int x, int y, Tilemap tilemap)
    {
        foreach (var rule in Rules)
        {
            if (CheckRule(rule, x, y, tilemap)) return rule.Sprite;
        }
        return DefaultSprite;
    }

    private bool CheckRule(Rule rule, int x, int y, Tilemap tilemap)
    {
        int index = 0;
        for (int dy = 1; dy >= -1; dy--)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                var neighborType = rule.Neighbors[index++];
                if (neighborType == Neighbor.Any) continue;

                var otherTile = tilemap.GetTile(x + dx, y + dy);
                bool isMatch = otherTile != null && otherTile.GetType() == this.GetType();

                if (neighborType == Neighbor.Required && !isMatch) return false;
                if (neighborType == Neighbor.NotRequired && isMatch) return false;
            }
        }
        return true;
    }
}
