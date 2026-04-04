namespace HelpDesk.Logging;

public sealed class CorrelationContext : ICorrelationContext
{
    private Guid? _id;

    public Guid? Id => _id;

    public Guid BeginNew()
    {
        var g = Guid.NewGuid();
        _id = g;
        return g;
    }

    public void CancelCorrelation() => _id = null;
}

