namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// In-memory log of model calls for eval, read per case with a mark and a since call.
/// </summary>
public sealed class LlmCallLog
{
    private readonly object _lock = new object();
    private readonly List<LlmCallSample> _samples = new List<LlmCallSample>();

    public bool CaptureText { get; set; }

    /// <summary>
    /// Adds a model call sample to the log.
    /// </summary>
    public void Add(LlmCallSample sample)
    {
        lock (_lock)
        {
            _samples.Add(sample);
        }
    }

    /// <summary>
    /// Returns a marker for the current end of the log.
    /// </summary>
    public int Mark()
    {
        lock (_lock)
        {
            return _samples.Count;
        }
    }

    /// <summary>
    /// Returns the samples recorded since the marker.
    /// </summary>
    public IReadOnlyList<LlmCallSample> Since(int mark)
    {
        lock (_lock)
        {
            return _samples.Skip(mark).ToList();
        }
    }
}
