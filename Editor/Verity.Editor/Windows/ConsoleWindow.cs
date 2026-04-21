using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core;

namespace Verity.Editor.Windows;

public class ConsoleWindow : EditorWindow
{
    private const int MaxLogEntries = 1000;

    private readonly EditorApp _app;

    private struct LogEntry
    {
        public string Message;
        public LogLevel Level;
    }

    private static readonly List<LogEntry> _entries = [];
    private static readonly HashSet<int> _selectedIndices = [];
    private static int _dragStartIndex = -1;

    public ConsoleWindow(EditorApp app) : base(L10n.Tr("window_console"))
    {
        _app = app;
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        _entries.Add(new LogEntry
        {
            Message = $"[{DateTime.Now:HH:mm:ss}] {message}",
            Level = level
        });

        TrimEntriesIfNeeded();
    }

    public static void Clear()
    {
        _entries.Clear();
        _selectedIndices.Clear();
        _dragStartIndex = -1;
    }

    public override void OnGui()
    {
        if (ImGui.Button(L10n.Tr("btn_clear")))
            Clear();
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_copy_all")))
        {
            var all = string.Join("\n", _entries.Select(e => e.Message));
            ImGui.SetClipboardText(all);
            NotifyCopyCompleted();
        }
        if (_selectedIndices.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("ctx_copy_all_selected") + $" ({_selectedIndices.Count})"))
            {
                CopySelectedToClipboard();
            }

            if (ImGui.IsWindowFocused() && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C))
            {
                CopySelectedToClipboard();
            }
        }

        ImGui.Separator();

        if (ImGui.BeginChild("ConsoleScrollRegion"))
        {
            float lineHeight = ImGui.GetTextLineHeightWithSpacing();
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var color = entry.Level switch
                {
                    LogLevel.Warning => new Vector4(1f, 0.9f, 0.3f, 1f),
                    LogLevel.Error => new Vector4(1f, 0.3f, 0.3f, 1f),
                    _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
                };

                ImGui.PushID(i);
                bool isSelected = _selectedIndices.Contains(i);

                if (ImGui.Selectable($"##log_{i}", isSelected, ImGuiSelectableFlags.AllowOverlap, new Vector2(0, lineHeight)))
                {
                    if (!ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
                    {
                        _selectedIndices.Clear();
                        _selectedIndices.Add(i);
                    }
                    else if (ImGui.GetIO().KeyCtrl)
                    {
                        if (isSelected) _selectedIndices.Remove(i);
                        else _selectedIndices.Add(i);
                    }
                    else if (ImGui.GetIO().KeyShift && _selectedIndices.Count > 0)
                    {
                        int min = _selectedIndices.Min();
                        int max = _selectedIndices.Max();
                        int start = Math.Min(i, min);
                        int end = Math.Max(i, max);
                        _selectedIndices.Clear();
                        for (int j = start; j <= end; j++) _selectedIndices.Add(j);
                    }
                }

                // Drag Selection Logic
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    if (_dragStartIndex == -1) _dragStartIndex = i;
                    
                    int currentIndex = i;
                    int start = Math.Min(_dragStartIndex, currentIndex);
                    int end = Math.Max(_dragStartIndex, currentIndex);

                    if (!ImGui.GetIO().KeyCtrl) _selectedIndices.Clear();
                    for (int j = start; j <= end; j++) _selectedIndices.Add(j);
                }

                ImGui.SameLine(0, 4);
                ImGui.TextColored(color, entry.Message);

                if (ImGui.BeginPopupContextItem($"context_{i}"))
                {
                    if (ImGui.MenuItem(L10n.Tr("ctx_copy_message")))
                    {
                        ImGui.SetClipboardText(entry.Message);
                        NotifyCopyCompleted();
                    }
                    
                    if (_selectedIndices.Count > 1 && ImGui.MenuItem(L10n.Tr("ctx_copy_all_selected")))
                    {
                        CopySelectedToClipboard();
                    }

                    if (ImGui.MenuItem(L10n.Tr("ctx_clear_console")))
                        Clear();
                    
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _dragStartIndex = -1;
            }

            if (ImGui.IsWindowFocused() && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
            {
                if (!ImGui.IsAnyItemHovered()) _selectedIndices.Clear();
            }

            if (_entries.Count > 0 && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_console"); }

    private void CopySelectedToClipboard()
    {
        var selected = _entries.Where((e, idx) => _selectedIndices.Contains(idx)).Select(e => e.Message);
        ImGui.SetClipboardText(string.Join("\n", selected));
        NotifyCopyCompleted();
    }

    private void NotifyCopyCompleted()
    {
        _app.ShowOverlayMessage(L10n.Tr("msg_console_copied"));
    }

    private static void TrimEntriesIfNeeded()
    {
        int removeCount = _entries.Count - MaxLogEntries;
        if (removeCount <= 0)
            return;

        _entries.RemoveRange(0, removeCount);

        var adjustedSelections = _selectedIndices
            .Where(index => index >= removeCount)
            .Select(index => index - removeCount)
            .ToList();

        _selectedIndices.Clear();
        foreach (int index in adjustedSelections)
            _selectedIndices.Add(index);

        if (_dragStartIndex >= 0)
            _dragStartIndex = _dragStartIndex >= removeCount ? _dragStartIndex - removeCount : -1;
    }
}
