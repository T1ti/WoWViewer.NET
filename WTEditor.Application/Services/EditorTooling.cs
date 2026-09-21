namespace WTEditor.Application.Services;

public interface IEditorTool
{
    string Id { get; }
    string DisplayName { get; }
    void Activate();
    void Deactivate();
}

public sealed class ToolManager
{
    private readonly Dictionary<string, IEditorTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public IEditorTool? ActiveTool { get; private set; }
    public IReadOnlyCollection<IEditorTool> Tools => _tools.Values;
    public event EventHandler<IEditorTool?>? ActiveToolChanged;

    public void Register(IEditorTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_tools.TryAdd(tool.Id, tool))
            throw new InvalidOperationException($"A tool with id '{tool.Id}' is already registered.");
    }

    public void Activate(string id)
    {
        if (!_tools.TryGetValue(id, out var next))
            throw new KeyNotFoundException($"No editor tool is registered with id '{id}'.");

        if (ReferenceEquals(ActiveTool, next))
            return;

        var previous = ActiveTool;
        previous?.Deactivate();
        try
        {
            next.Activate();
        }
        catch (Exception activationException)
        {
            if (previous != null)
            {
                try
                {
                    previous.Activate();
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        $"Tool '{next.Id}' failed to activate and the previous tool could not be restored.",
                        activationException,
                        rollbackException);
                }
            }

            throw;
        }

        ActiveTool = next;
        ActiveToolChanged?.Invoke(this, next);
    }
}
