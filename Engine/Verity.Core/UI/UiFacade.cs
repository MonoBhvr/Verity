using Verity.Core.World;

namespace Verity.Core.UI;

public sealed class WorldUi
{
    private readonly World.World _world;

    internal WorldUi(World.World world)
    {
        _world = world;
    }

    public Canvas Open(UIScreenAsset screen) => UiSystem.ShowScreen(screen, _world);
    public Canvas OpenRole(string role) => UiSystem.ShowRole(role, _world);
    public Canvas Open(string path, string? guid = null) => UiSystem.ShowScreen(UiSystem.LoadAsset(path, guid), _world);
    public void Close(string screenNameOrId) => UiSystem.HideScreen(screenNameOrId, _world);
    public void CloseRole(string role) => UiSystem.HideRole(role, _world);
    public Canvas? Find(string screenNameOrId) => UiSystem.FindCanvas(screenNameOrId, _world);
    public Canvas? FindRole(string role) => UiSystem.FindCanvasByRole(role, _world);
    public IReadOnlyList<Canvas> GetCanvases() => UiSystem.GetCanvases(_world);

    public void Set(string screenNameOrId, string variableName, object? value)
    {
        Find(screenNameOrId)?.Set(variableName, value);
    }

    public void SetRole(string role, string variableName, object? value)
    {
        FindRole(role)?.Set(variableName, value);
    }

    public void Send(string screenNameOrId, string command, object? payload = null)
    {
        Find(screenNameOrId)?.Send(command, payload);
    }

    public void SendRole(string role, string command, object? payload = null)
    {
        FindRole(role)?.Send(command, payload);
    }
}

public static class UI
{
    private static WorldUi ActiveUi =>
        WorldManager.ActiveWorld?.Ui ?? throw new InvalidOperationException("There is no active world.");

    public static Canvas Open(UIScreenAsset screen) => ActiveUi.Open(screen);
    public static Canvas OpenRole(string role) => ActiveUi.OpenRole(role);
    public static Canvas Open(string path, string? guid = null) => ActiveUi.Open(path, guid);
    public static void Close(string screenNameOrId) => ActiveUi.Close(screenNameOrId);
    public static void CloseRole(string role) => ActiveUi.CloseRole(role);
    public static Canvas? Find(string screenNameOrId) => ActiveUi.Find(screenNameOrId);
    public static Canvas? FindRole(string role) => ActiveUi.FindRole(role);
    public static void Set(string screenNameOrId, string variableName, object? value) => ActiveUi.Set(screenNameOrId, variableName, value);
    public static void SetRole(string role, string variableName, object? value) => ActiveUi.SetRole(role, variableName, value);
    public static void Send(string screenNameOrId, string command, object? payload = null) => ActiveUi.Send(screenNameOrId, command, payload);
    public static void SendRole(string role, string command, object? payload = null) => ActiveUi.SendRole(role, command, payload);
}
