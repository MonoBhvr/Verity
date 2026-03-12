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
}

internal sealed class UndoSystem
{
    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();
    private const int MaxHistory = 100;

    private UndoState? _pendingSnapshot;

    private UndoState CreateSnapshot(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        return new UndoState
        {
            WorldJson = SceneSerializer.Serialize(world),
            ProjectSettingsJson = JsonSerializer.Serialize(projectSettings),
            BuildSettingsJson = JsonSerializer.Serialize(buildSettings)
        };
    }

    public void Record(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        var current = CreateSnapshot(world, projectSettings, buildSettings);
        if (_undoStack.Count > 0 && 
            _undoStack.Peek().WorldJson == current.WorldJson && 
            _undoStack.Peek().ProjectSettingsJson == current.ProjectSettingsJson &&
            _undoStack.Peek().BuildSettingsJson == current.BuildSettingsJson) return;

        _undoStack.Push(current);
        _redoStack.Clear();
        _pendingSnapshot = null;

        LimitStack(_undoStack);
    }

    public void BeginContinuousAction(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        if (_pendingSnapshot == null)
        {
            _pendingSnapshot = CreateSnapshot(world, projectSettings, buildSettings);
        }
    }

    public void EndContinuousAction(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        if (_pendingSnapshot != null)
        {
            var current = CreateSnapshot(world, projectSettings, buildSettings);
            if (current.WorldJson != _pendingSnapshot.WorldJson || 
                current.ProjectSettingsJson != _pendingSnapshot.ProjectSettingsJson ||
                current.BuildSettingsJson != _pendingSnapshot.BuildSettingsJson)
            {
                _undoStack.Push(_pendingSnapshot);
                _redoStack.Clear();
                LimitStack(_undoStack);
            }
            _pendingSnapshot = null;
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

    public UndoState? Undo(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        if (_undoStack.Count == 0) return null;

        var current = CreateSnapshot(world, projectSettings, buildSettings);
        _redoStack.Push(current);
        LimitStack(_redoStack);

        var last = _undoStack.Pop();
        // Skip if same
        if (last.WorldJson == current.WorldJson && 
            last.ProjectSettingsJson == current.ProjectSettingsJson && 
            last.BuildSettingsJson == current.BuildSettingsJson && 
            _undoStack.Count > 0)
        {
            last = _undoStack.Pop();
        }

        return last;
    }

    public UndoState? Redo(World world, ProjectSettings projectSettings, BuildSettings buildSettings)
    {
        if (_redoStack.Count == 0) return null;

        var current = CreateSnapshot(world, projectSettings, buildSettings);
        _undoStack.Push(current);
        LimitStack(_undoStack);

        return _redoStack.Pop();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingSnapshot = null;
    }
}
