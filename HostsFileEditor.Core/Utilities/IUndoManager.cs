namespace HostsFileEditor.Utilities;

/// <summary>
/// Abstraction over <see cref="UndoManager"/>'s instance surface — extracted so
/// consumers can eventually depend on this instead of the static <c>Instance</c>
/// accessor, without changing any behavior today.
/// </summary>
public interface IUndoManager
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    event EventHandler? HistoryChanged;

    void BatchActions(Action action);

    void AddActions(Action undoAction, Action redoAction);

    void Undo();

    void Redo();

    void ClearHistory();

    void SuspendUndo(Action action);

    void SuspendRedo(Action action);

    void SuspendUndoRedo(Action action);
}
