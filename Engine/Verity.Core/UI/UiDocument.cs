using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.UI;

public sealed class UiDocument : Script
{
    private readonly HashSet<string> _registeredKeys = new(StringComparer.OrdinalIgnoreCase);

    [AssetReference(".ui")]
    public string ScreenPath { get; set; } = string.Empty;

    public string ScreenGuid { get; set; } = string.Empty;
    public string BindingNamespace { get; set; } = string.Empty;
    public bool AutoShow { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool BindOwnerEntity { get; set; } = true;
    public bool BindOwnerComponents { get; set; } = true;

    [HideInInspector]
    public Canvas? Canvas { get; private set; }

    [HideInInspector]
    public UIScreenAsset? Screen { get; private set; }

    public override void Awake()
    {
        RegisterBindings();
        if (AutoShow)
            Show();
    }

    public override void Update()
    {
        if (Canvas != null)
            Canvas.Visible = Enabled && Owner.Active && Visible;
    }

    public Canvas? Show()
    {
        if (Canvas != null)
            return Canvas;

        if (string.IsNullOrWhiteSpace(ScreenPath))
            return null;

        Screen = UiSystem.LoadAsset(ScreenPath, ScreenGuid);
        Canvas = UiSystem.ShowScreen(Screen, Owner.World, Owner);
        Canvas.Visible = Enabled && Owner.Active && Visible;
        RegisterBindings();
        return Canvas;
    }

    public void Hide()
    {
        if (Canvas == null)
            return;

        UiSystem.HideCanvas(Canvas);
        Canvas = null;
        Screen = null;
    }

    public void Reload()
    {
        Hide();
        Show();
    }

    public T? Query<T>(string nameOrId) where T : UiNode => Canvas?.Query<T>(nameOrId);
    public UiNode? Query(string nameOrId) => Canvas?.Query(nameOrId);

    public void BindSource(string key, object source)
    {
        RegisterKey(key, source);
    }

    public void UnbindSource(string key)
    {
        UiSystem.Unbind(key);
        _registeredKeys.Remove(key);
    }

    public override void OnDestroy()
    {
        Hide();
        UnregisterBindings();
        base.OnDestroy();
    }

    private void RegisterBindings()
    {
        string prefix = ResolveBindingNamespace();
        if (BindOwnerEntity)
        {
            RegisterKey(prefix, Owner);
            RegisterKey($"{prefix}:Entity", Owner);
        }

        if (BindOwnerComponents)
        {
            foreach (var component in Owner.GetAllComponents())
            {
                if (component is Transform)
                    continue;

                RegisterKey($"{prefix}:{component.GetType().Name}", component);
            }
        }
    }

    private void UnregisterBindings()
    {
        foreach (string key in _registeredKeys)
            UiSystem.Unbind(key);
        _registeredKeys.Clear();
    }

    private void RegisterKey(string key, object source)
    {
        if (string.IsNullOrWhiteSpace(key) || source == null)
            return;

        UiSystem.Bind(key, source);
        _registeredKeys.Add(key);
    }

    private string ResolveBindingNamespace()
    {
        if (!string.IsNullOrWhiteSpace(BindingNamespace))
            return BindingNamespace;

        return string.IsNullOrWhiteSpace(Owner.Name)
            ? "UiDocument"
            : Owner.Name.Replace(' ', '_');
    }
}
