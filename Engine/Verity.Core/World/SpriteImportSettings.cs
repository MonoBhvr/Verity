using System.Numerics;
using System.Linq;
using System.Text.Json.Serialization;
using Verity.Core.Engine;
using Verity.Core.Serialization;

namespace Verity.Core.World;

public enum SpriteTextureFilter
{
    Point,
    Linear
}

public enum SpriteImportMode
{
    Single,
    Multiple
}

public enum SpriteSizingMode
{
    FitInsideUnit,
    PixelsPerUnit
}

public sealed class SpriteImportSettings
{
    public SpriteTextureFilter Filter { get; set; } = SpriteTextureFilter.Point;
    public SpriteImportMode SpriteMode { get; set; } = SpriteImportMode.Single;
    public SpriteSizingMode SizeMode { get; set; } = SpriteSizingMode.FitInsideUnit;
    public int PixelsPerUnit { get; set; } = 32;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 DefaultPivot { get; set; } = new(0.5f, 0.5f);

    public List<SpriteSlice> Slices { get; set; } = new();

    public void Normalize(int textureWidth, int textureHeight)
    {
        PixelsPerUnit = Math.Max(1, PixelsPerUnit);
        DefaultPivot = SpriteImportUtility.ClampPivot(DefaultPivot);

        if (SpriteMode == SpriteImportMode.Single)
        {
            if (Slices.Count == 0)
            {
                Slices.Add(SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, DefaultPivot));
            }
            else
            {
                var first = Slices[0];
                first.Name = string.IsNullOrWhiteSpace(first.Name) ? "Sprite" : first.Name;
                if (first.Width <= 0 || first.Height <= 0)
                {
                    first.X = 0;
                    first.Y = 0;
                    first.Width = Math.Max(1, textureWidth);
                    first.Height = Math.Max(1, textureHeight);
                }

                first.Pivot = SpriteImportUtility.ClampPivot(first.Pivot);
                first.EnsureId();
                Slices = [first];
            }

            return;
        }

        for (int i = 0; i < Slices.Count; i++)
        {
            Slices[i].EnsureId();
            Slices[i].Name = string.IsNullOrWhiteSpace(Slices[i].Name) ? $"Sprite {i + 1}" : Slices[i].Name;
            Slices[i].Width = Math.Max(1, Slices[i].Width);
            Slices[i].Height = Math.Max(1, Slices[i].Height);
            Slices[i].Pivot = SpriteImportUtility.ClampPivot(Slices[i].Pivot);
        }
    }

    public SpriteImportSettings Clone()
    {
        return new SpriteImportSettings
        {
            Filter = Filter,
            SpriteMode = SpriteMode,
            SizeMode = SizeMode,
            PixelsPerUnit = PixelsPerUnit,
            DefaultPivot = DefaultPivot,
            Slices = Slices.Select(slice => slice.Clone()).ToList()
        };
    }
}

public sealed class SpriteSlice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Sprite";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    public void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");
    }

    public SpriteSlice Clone()
    {
        return new SpriteSlice
        {
            Id = Id,
            Name = Name,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Pivot = Pivot
        };
    }
}

public static class SpriteImportUtility
{
    public static SpriteImportSettings CreateDefaults(ProjectSettings? settings, int textureWidth, int textureHeight)
    {
        int ppu = Math.Max(1, settings?.DefaultSpritePixelsPerUnit ?? 32);
        int pointThreshold = Math.Max(1, settings?.DefaultPointFilterMaxDimension ?? 256);

        var import = new SpriteImportSettings
        {
            Filter = Math.Max(textureWidth, textureHeight) <= pointThreshold ? SpriteTextureFilter.Point : SpriteTextureFilter.Linear,
            PixelsPerUnit = ppu,
            SizeMode = settings?.DefaultSpriteSizeMode ?? SpriteSizingMode.FitInsideUnit,
            DefaultPivot = new Vector2(0.5f, 0.5f)
        };
        import.Slices.Add(CreateDefaultSlice(textureWidth, textureHeight, import.DefaultPivot));
        return import;
    }

    public static SpriteSlice CreateDefaultSlice(int textureWidth, int textureHeight, Vector2 pivot)
    {
        return new SpriteSlice
        {
            Name = "Sprite",
            X = 0,
            Y = 0,
            Width = Math.Max(1, textureWidth),
            Height = Math.Max(1, textureHeight),
            Pivot = ClampPivot(pivot)
        };
    }

    public static Vector2 ClampPivot(Vector2 value)
    {
        return new Vector2(Math.Clamp(value.X, 0f, 1f), Math.Clamp(value.Y, 0f, 1f));
    }

    public static Vector2 ComputeWorldSize(SpriteImportSettings settings, SpriteSlice slice)
    {
        int width = Math.Max(1, slice.Width);
        int height = Math.Max(1, slice.Height);

        if (settings.SizeMode == SpriteSizingMode.PixelsPerUnit)
        {
            float ppu = Math.Max(1, settings.PixelsPerUnit);
            return new Vector2(width / ppu, height / ppu);
        }

        float maxDimension = MathF.Max(width, height);
        return new Vector2(width / maxDimension, height / maxDimension);
    }
}
