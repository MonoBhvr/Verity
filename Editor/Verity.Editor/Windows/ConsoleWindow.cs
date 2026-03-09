using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core;

namespace Verity.Editor.Windows;

public class ConsoleWindow : EditorWindow
{
    private struct LogEntry
    {
        public string Message;
        public LogLevel Level;
    }

    private static readonly List<LogEntry> _entries = [];

    public ConsoleWindow() : base("Console") { }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        _entries.Add(new LogEntry
        {
            Message = $"[{DateTime.Now:HH:mm:ss}] {message}",
            Level = level
        });
    }

    public static void Clear()
    {
        _entries.Clear();
    }

    public override void OnGui()
    {
        if (ImGui.Button("Clear"))
            Clear();

        ImGui.Separator();

        ImGui.BeginChild("ConsoleScrollRegion");
        foreach (var entry in _entries)
        {
            var color = entry.Level switch
            {
                LogLevel.Warning => new Vector4(1f, 0.9f, 0.3f, 1f),
                LogLevel.Error => new Vector4(1f, 0.3f, 0.3f, 1f),
                _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
            };
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(entry.Message);
            ImGui.PopStyleColor();
        }

        if (_entries.Count > 0)
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }
}
