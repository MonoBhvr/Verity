using System.Reflection;
using System.Text.Json;
using Verity.Core.World;
using Verity.Core.Serialization;
using Verity.Core.Engine;

namespace Verity.Editor;

internal class UndoState
{
    public string WorldJson { get; set; } = "";
    public string ProjectSettingsJson { get; set; } = "";
    public string BuildSettingsJson { get; set; } = "";
    public string EditorStateJson { get; set; } = "";
}

internal sealed class UndoSystem
{
    private sealed class UndoHistory
    {
        public Stack<UndoState> UndoStack { get; } = new();
        public Stack<UndoState> RedoStack { get; } = new();
        public UndoState? PendingSnapshot { get; set; }
    }

    private readonly Dictionary<string, UndoHistory> _histories = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxHistory = 100;

    private UndoState CreateSnapshot(World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        return new UndoState
        {
            WorldJson = SceneSerializer.Serialize(world),
            ProjectSettingsJson = JsonSerializer.Serialize(projectSettings),
            BuildSettingsJson = JsonSerializer.Serialize(buildSettings),
            EditorStateJson = editorStateJson
        };
    }

    private UndoHistory GetHistory(string scopeKey)
    {
        if (!_histories.TryGetValue(scopeKey, out UndoHistory? history))
        {
            history = new UndoHistory();
            _histories[scopeKey] = history;
        }

        return history;
    }

    public void Record(string scopeKey, World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        UndoHistory history = GetHistory(scopeKey);
        var current = CreateSnapshot(world, projectSettings, buildSettings, editorStateJson);
        if (history.UndoStack.Count > 0 &&
            history.UndoStack.Peek().WorldJson == current.WorldJson &&
            history.UndoStack.Peek().ProjectSettingsJson == current.ProjectSettingsJson &&
            history.UndoStack.Peek().BuildSettingsJson == current.BuildSettingsJson &&
            history.UndoStack.Peek().EditorStateJson == current.EditorStateJson) return;

        history.UndoStack.Push(current);
        history.RedoStack.Clear();
        history.PendingSnapshot = null;

        LimitStack(history.UndoStack);
    }

    public void BeginContinuousAction(string scopeKey, World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        UndoHistory history = GetHistory(scopeKey);
        history.PendingSnapshot ??= CreateSnapshot(world, projectSettings, buildSettings, editorStateJson);
    }

    public void EndContinuousAction(string scopeKey, World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        UndoHistory history = GetHistory(scopeKey);
        if (history.PendingSnapshot != null)
        {
            var current = CreateSnapshot(world, projectSettings, buildSettings, editorStateJson);
            if (current.WorldJson != history.PendingSnapshot.WorldJson ||
                current.ProjectSettingsJson != history.PendingSnapshot.ProjectSettingsJson ||
                current.BuildSettingsJson != history.PendingSnapshot.BuildSettingsJson ||
                current.EditorStateJson != history.PendingSnapshot.EditorStateJson)
            {
                history.UndoStack.Push(history.PendingSnapshot);
                history.RedoStack.Clear();
                LimitStack(history.UndoStack);
            }
            history.PendingSnapshot = null;
        }
    }

    private void LimitStack<T>(Stack<T> stack)
    {
        if (stack.Count > MaxHistory)
        {
            var list = stack.ToList();
            list.RemoveAt(list.Count - 1);
            stack.Clear();
            for (int i = list.Count - 1; i >= 0; i--) stack.Push(list[i]);
        }
    }

    public UndoState? Undo(string scopeKey, World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        UndoHistory history = GetHistory(scopeKey);
        if (history.UndoStack.Count == 0) return null;

        var current = CreateSnapshot(world, projectSettings, buildSettings, editorStateJson);
        history.RedoStack.Push(current);
        LimitStack(history.RedoStack);

        var last = history.UndoStack.Pop();
        // Skip if same
        if (last.WorldJson == current.WorldJson &&
            last.ProjectSettingsJson == current.ProjectSettingsJson &&
            last.BuildSettingsJson == current.BuildSettingsJson &&
            last.EditorStateJson == current.EditorStateJson &&
            history.UndoStack.Count > 0)
        {
            last = history.UndoStack.Pop();
        }

        return last;
    }

    public UndoState? Redo(string scopeKey, World world, ProjectSettings projectSettings, BuildSettings buildSettings, string editorStateJson)
    {
        UndoHistory history = GetHistory(scopeKey);
        if (history.RedoStack.Count == 0) return null;

        var current = CreateSnapshot(world, projectSettings, buildSettings, editorStateJson);
        history.UndoStack.Push(current);
        LimitStack(history.UndoStack);

        return history.RedoStack.Pop();
    }

    public void Clear()
    {
        _histories.Clear();
    }
}
