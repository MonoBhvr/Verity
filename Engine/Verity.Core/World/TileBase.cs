using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.World;

/// <summary>
/// 모든 타일 유형의 기초가 되는 추상 클래스
/// </summary>
public abstract class TileBase
{
    [HideInInspector]
    public string? AssetPath { get; set; }

    [HideInInspector]
    public string? AssetGuid { get; set; }
    public string Name { get; set; } = "New Tile";
    public bool IsCollidable { get; set; } = true;
    public Color Color { get; set; } = Color.White;

    /// <summary>
    /// 해당 위치와 상황에 맞는 스프라이트를 반환합니다.
    /// </summary>
    public abstract Sprite? GetSprite(int x, int y, Tilemap tilemap);
}

/// <summary>
/// 가장 기본적인 단일 스프라이트 타일
/// </summary>
public class Tile : TileBase
{
    public Sprite? Sprite { get; set; }

    public override Sprite? GetSprite(int x, int y, Tilemap tilemap) => Sprite;
}
