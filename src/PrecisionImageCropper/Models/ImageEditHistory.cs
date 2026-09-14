namespace PrecisionImageCropper.Models;

/// <summary>Bounded undo/redo history for one image's edit recipe.</summary>
public sealed class ImageEditHistory
{
    private readonly int _capacity;
    private readonly Stack<ImageEditState> _undo = [];
    private readonly Stack<ImageEditState> _redo = [];

    public ImageEditHistory(int capacity = 50) => _capacity = Math.Max(1, capacity);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Commit(ImageEditState before, ImageEditState after)
    {
        if (before.IsEquivalentTo(after)) return;
        _undo.Push(before);
        while (_undo.Count > _capacity)
            RemoveOldest(_undo);
        _redo.Clear();
    }

    public ImageEditState? Undo(ImageEditState current)
    {
        if (_undo.Count == 0) return null;
        var previous = _undo.Pop();
        _redo.Push(current);
        return previous;
    }

    public ImageEditState? Redo(ImageEditState current)
    {
        if (_redo.Count == 0) return null;
        var next = _redo.Pop();
        _undo.Push(current);
        return next;
    }

    private static void RemoveOldest(Stack<ImageEditState> stack)
    {
        // Stack enumeration is newest first. Rebuild from oldest to newest so
        // the most recent state remains on top.
        var retained = stack.Take(stack.Count - 1).Reverse().ToArray();
        stack.Clear();
        foreach (var state in retained) stack.Push(state);
    }
}
