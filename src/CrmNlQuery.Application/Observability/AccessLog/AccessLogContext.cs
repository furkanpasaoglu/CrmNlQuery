namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Ambient context carrying the source and request id of the current data read so access-log rows can be grouped by question.
/// </summary>
public static class AccessLogContext
{
    private sealed record Frame(string Source, Guid RequestId);

    private static readonly AsyncLocal<Frame?> Current = new AsyncLocal<Frame?>();

    public static string Source => Current.Value?.Source ?? "other";

    public static Guid? RequestId => Current.Value?.RequestId;

    /// <summary>
    /// Starts an access-log scope with the given source and optional request id and returns a disposable that restores the previous scope.
    /// </summary>
    public static IDisposable Begin(string source, Guid? requestId = null)
    {
        var previous = Current.Value;
        Current.Value = new Frame(source, requestId ?? Guid.NewGuid());
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Frame? _previous;
        public Scope(Frame? previous) { _previous = previous; }
        /// <summary>
        /// Ends the access-log scope and restores the previous context.
        /// </summary>
        public void Dispose() { Current.Value = _previous; }
    }
}
