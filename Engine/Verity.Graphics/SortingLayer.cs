namespace Verity.Graphics;

public static class SortingLayer
{
    private static readonly List<string> _layers = ["Default"];

    public static IReadOnlyList<string> Layers => _layers;

    public static int GetLayerIndex(string layerName)
    {
        var idx = _layers.IndexOf(layerName);
        return idx >= 0 ? idx : 0;
    }

    public static void AddLayer(string layerName)
    {
        if (!_layers.Contains(layerName))
            _layers.Add(layerName);
    }

    public static void InsertLayer(int index, string layerName)
    {
        if (!_layers.Contains(layerName))
            _layers.Insert(Math.Clamp(index, 0, _layers.Count), layerName);
    }

    public static void RemoveLayer(string layerName)
    {
        if (layerName != "Default")
            _layers.Remove(layerName);
    }

    public static void Reset()
    {
        _layers.Clear();
        _layers.Add("Default");
    }
}
