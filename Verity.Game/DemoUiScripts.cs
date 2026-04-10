using System.Collections;
using Verity.Core.UI;

namespace Verity.Game;

public sealed class DemoHudUiScript : UiScript
{
    public override void OnOpen()
    {
        UpdateHealthRatio();
        SetState("HealthRatio", 1f);
    }

    public override void OnVariableChanged(string name, object? value)
    {
        if (string.Equals(name, "Health", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "MaxHealth", StringComparison.OrdinalIgnoreCase))
        {
            UpdateHealthRatio();
        }
    }

    public override void OnCommand(string command, object? payload)
    {
        if (string.Equals(command, "FlashHit", StringComparison.OrdinalIgnoreCase))
            SetState("LastCommand", "FlashHit");
    }

    private void UpdateHealthRatio()
    {
        float health = ReadFloat("Health", 100f);
        float maxHealth = Math.Max(1f, ReadFloat("MaxHealth", 100f));
        SetState("HealthRatio", Math.Clamp(health / maxHealth, 0f, 1f));
    }

    private float ReadFloat(string name, float fallback)
    {
        return Canvas.TryGetVariable(name, out object? value) && value != null
            ? Convert.ToSingle(value)
            : fallback;
    }
}

public sealed class DemoInventoryUiScript : UiScript
{
    public override void OnOpen()
    {
        SetState("CurrentTab", "Items");
        SetState("SelectedIndex", -1);
        SetState("VisibleCount", CountItems());
    }

    public override void OnVariableChanged(string name, object? value)
    {
        if (string.Equals(name, "Items", StringComparison.OrdinalIgnoreCase))
            SetState("VisibleCount", CountItems());
    }

    public override void OnCommand(string command, object? payload)
    {
        if (string.Equals(command, "OpenTab", StringComparison.OrdinalIgnoreCase))
        {
            SetState("CurrentTab", payload?.ToString() ?? "Items");
            return;
        }

        if (string.Equals(command, "SelectIndex", StringComparison.OrdinalIgnoreCase) && payload != null)
        {
            SetState("SelectedIndex", Convert.ToInt32(payload));
        }
    }

    private int CountItems()
    {
        if (!Canvas.TryGetVariable("Items", out object? value) || value is string || value is not IEnumerable enumerable)
            return 0;

        int count = 0;
        foreach (object? _ in enumerable)
            count++;
        return count;
    }
}
