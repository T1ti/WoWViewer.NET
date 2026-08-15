namespace WTEditor.Application.Services;

/// <summary>
/// Reversible unit of editor state mutation. Commands are the only supported
/// way for tools to participate in the shared editor history.
/// </summary>
public interface IEditorCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

/// <summary>
/// Groups a continuous user gesture into one history entry. Dispose without
/// Commit to cancel and roll back commands already added to the transaction.
/// </summary>
public interface IUndoTransaction : IDisposable
{
    void Commit();
}

public sealed class UndoService
{
    private readonly Stack<IEditorCommand> _undo = [];
    private readonly Stack<IEditorCommand> _redo = [];
    private Transaction? _transaction;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var command) ? command.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var command) ? command.Description : null;
    public event EventHandler? HistoryChanged;

    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        RecordExecuted(command);
    }

    /// <summary>
    /// Records an action whose forward operation has already been applied by
    /// an external system, such as the renderer. This keeps the history
    /// universal without applying a terrain stroke twice when it ends.
    /// </summary>
    public void RecordExecuted(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _redo.Clear();

        if (_transaction != null)
            _transaction.Commands.Add(command);
        else
            _undo.Push(command);

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        EnsureNoActiveTransaction();
        if (!_undo.TryPop(out var command))
            return;

        command.Undo();
        _redo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        EnsureNoActiveTransaction();
        if (!_redo.TryPop(out var command))
            return;

        command.Execute();
        _undo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public IUndoTransaction BeginTransaction(string description)
    {
        EnsureNoActiveTransaction();
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _transaction = new Transaction(this, description);
        return _transaction;
    }

    public void Clear()
    {
        EnsureNoActiveTransaction();
        _undo.Clear();
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureNoActiveTransaction()
    {
        if (_transaction != null)
            throw new InvalidOperationException("Complete the active undo transaction first.");
    }

    private sealed class Transaction(UndoService owner, string description) : IUndoTransaction
    {
        public List<IEditorCommand> Commands { get; } = [];
        private bool _committed;
        private bool _disposed;

        public void Commit()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Transaction));

            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner._transaction = null;

            if (_committed)
            {
                if (Commands.Count > 0)
                    owner._undo.Push(new CompositeCommand(description, Commands.ToArray()));
            }
            else
            {
                for (var index = Commands.Count - 1; index >= 0; index--)
                    Commands[index].Undo();
            }

            owner.HistoryChanged?.Invoke(owner, EventArgs.Empty);
        }
    }

    private sealed class CompositeCommand(string description, IReadOnlyList<IEditorCommand> commands) : IEditorCommand
    {
        public string Description => description;

        public void Execute()
        {
            foreach (var command in commands)
                command.Execute();
        }

        public void Undo()
        {
            for (var index = commands.Count - 1; index >= 0; index--)
                commands[index].Undo();
        }
    }
}

public sealed class DelegateEditorCommand(
    string description,
    Action execute,
    Action undo) : IEditorCommand
{
    public string Description { get; } = description;

    public void Execute() => execute();
    public void Undo() => undo();
}
