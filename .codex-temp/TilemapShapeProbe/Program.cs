using Verity.Core.Physics;
using Verity.Core.World;

var world = new World("probe");
var entity = world.CreateEntity("Tilemap");
var shape = entity.AddComponent<TilemapShape>();
var tilemap = entity.GetComponent<Tilemap>()!;

var tile = new Tile
{
    Name = "ProbeTile",
    IsCollidable = true
};

tilemap.SetTile(0, 0, tile);
tilemap.SetTile(1, 0, tile);
tilemap.SetTile(0, 1, tile);

var polygons = shape.GetWorldPolygons();
Console.WriteLine($"poly_count={polygons.Count}");
foreach (var polygon in polygons)
{
    Console.WriteLine(string.Join(" | ", polygon.Select(point => $"({point.X}, {point.Y})")));
}
