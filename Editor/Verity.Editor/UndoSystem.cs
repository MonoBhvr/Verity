using Verity.Core.World;

namespace Verity.Editor;

internal sealed class UndoSystem
{
    private readonly Stack<WorldSnapshot> _undoStack = new();
    private readonly Stack<WorldSnapshot> _redoStack = new();
    private const int MaxHistory = 100;

    private WorldSnapshot? _pendingSnapshot;

    public void Record(World world)
    {
        // Direct record (for discrete actions like Create/Delete)
        _undoStack.Push(WorldSnapshot.Capture(world));
        _redoStack.Clear();
        _pendingSnapshot = null;

        LimitStack(_undoStack);
    }

    public void BeginContinuousAction(World world)
    {
        // Capture state BEFORE the change starts
        if (_pendingSnapshot == null)
        {
            _pendingSnapshot = WorldSnapshot.Capture(world);
        }
    }

    public void EndContinuousAction(World world)
    {
        // If we have a pending snapshot, it means a change happened.
        // We push the BEFORE state to the undo stack.
        if (_pendingSnapshot != null)
        {
            _undoStack.Push(_pendingSnapshot);
            _redoStack.Clear();
            _pendingSnapshot = null;
            LimitStack(_undoStack);
        }
    }

    private void LimitStack(Stack<WorldSnapshot> stack)
    {
        if (stack.Count > MaxHistory)
        {
            var list = stack.ToList();
            list.RemoveAt(list.Count - 1);
            stack.Clear();
            for (int i = list.Count - 1; i >= 0; i--) stack.Push(list[i]);
        }
    }

    public void Undo(World world)
    {
        if (_undoStack.Count == 0) return;

        _redoStack.Push(WorldSnapshot.Capture(world));
        LimitStack(_redoStack);

        var snapshot = _undoStack.Pop();
        snapshot.Restore(world);
    }

    public void Redo(World world)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push(WorldSnapshot.Capture(world));
        LimitStack(_undoStack);

        var snapshot = _redoStack.Pop();
        snapshot.Restore(world);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _pendingSnapshot = null;
    }
}
