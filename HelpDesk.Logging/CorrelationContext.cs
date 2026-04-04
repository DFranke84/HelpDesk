namespace HelpDesk.Logging;

public sealed class CorrelationContext : ICorrelationContext
{
    private readonly object _lock = new();
    private Guid? _id;

    public Guid? Id { get { lock (_lock) return _id; } }

    public Guid BeginNew()
    {
        var g = Guid.NewGuid();
        lock (_lock) _id = g;
        return g;
    }

    public void CancelCorrelation()
    {
        lock (_lock) _id = null;
    }
}

