namespace CrmNlQuery.Application.Observability.Services;

/// <summary>
/// Correlates the model calls of one request under a shared id and collects per-request counters.
/// </summary>
public static class RequestContext
{
    private static readonly AsyncLocal<Guid> Id = new AsyncLocal<Guid>();
    private static readonly AsyncLocal<KnowledgeCounter?> Knowledge = new AsyncLocal<KnowledgeCounter?>();
    private static readonly AsyncLocal<ProgressState?> Progress = new AsyncLocal<ProgressState?>();
    private static readonly AsyncLocal<DeadlineWatch?> DeadlineToken = new AsyncLocal<DeadlineWatch?>();

    public static Guid Current => Id.Value;

    public static int? KnowledgePieces => Knowledge.Value is { Searched: true } counter ? counter.Pieces : null;

    /// <summary>
    /// Records that the pipeline searched the knowledge base, counting zero pieces as a search too.
    /// </summary>
    public static void RecordKnowledgeSearch(int pieces)
    {
        var counter = Knowledge.Value;
        if (counter is null)
        {
            return;
        }
        lock (counter)
        {
            counter.Searched = true;
            counter.Pieces += Math.Max(0, pieces);
        }
    }

    public static string Stage
    {
        get
        {
            var progress = Progress.Value;
            if (progress is null || progress.Stage is null)
            {
                return "unknown";
            }
            return progress.ChainStep > 1 ? "step" + progress.ChainStep + ":" + progress.Stage : progress.Stage;
        }
    }

    /// <summary>
    /// Sets the current pipeline stage of the request.
    /// </summary>
    public static void SetStage(string stage)
    {
        var progress = Progress.Value;
        if (progress is not null)
        {
            progress.Stage = stage;
        }
    }

    /// <summary>
    /// Sets the chain step number, where 1 is the first step and 2 and above are later steps.
    /// </summary>
    public static void SetChainStep(int step)
    {
        var progress = Progress.Value;
        if (progress is not null)
        {
            progress.ChainStep = step;
        }
    }

    public static bool DeadlineExpired => DeadlineToken.Value is { } watch && watch.Deadline.IsCancellationRequested && !watch.Caller.IsCancellationRequested;

    /// <summary>
    /// Watches the deadline and caller cancellation so an expired deadline is reported as a timeout.
    /// </summary>
    public static IDisposable WatchDeadline(CancellationToken deadline, CancellationToken caller = default)
    {
        var previous = DeadlineToken.Value;
        DeadlineToken.Value = new DeadlineWatch(deadline, caller);
        return new DeadlineScope(previous);
    }

    private sealed class DeadlineWatch
    {
        public readonly CancellationToken Deadline;
        public readonly CancellationToken Caller;
        public DeadlineWatch(CancellationToken deadline, CancellationToken caller)
        {
            Deadline = deadline;
            Caller = caller;
        }
    }

    /// <summary>
    /// Starts a request scope with the given correlation id.
    /// </summary>
    public static IDisposable Begin(Guid id)
    {
        var previousId = Id.Value;
        var previousKnowledge = Knowledge.Value;
        var previousProgress = Progress.Value;
        Id.Value = id;
        Knowledge.Value = new KnowledgeCounter();
        Progress.Value = new ProgressState();
        return new Scope(previousId, previousKnowledge, previousProgress);
    }

    private sealed class KnowledgeCounter
    {
        public bool Searched;
        public int Pieces;
    }

    private sealed class ProgressState
    {
        public string? Stage;
        public int ChainStep = 1;
    }

    private sealed class DeadlineScope : IDisposable
    {
        private readonly DeadlineWatch? _previous;
        public DeadlineScope(DeadlineWatch? previous)
        {
            _previous = previous;
        }
        /// <summary>
        /// Ends the deadline watch scope by restoring the previous ambient deadline token value.
        /// </summary>
        public void Dispose()
        {
            DeadlineToken.Value = _previous;
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid _previousId;
        private readonly KnowledgeCounter? _previousKnowledge;
        private readonly ProgressState? _previousProgress;
        public Scope(Guid previousId, KnowledgeCounter? previousKnowledge, ProgressState? previousProgress)
        {
            _previousId = previousId;
            _previousKnowledge = previousKnowledge;
            _previousProgress = previousProgress;
        }
        /// <summary>
        /// Ends the request scope and restores the previous context.
        /// </summary>
        public void Dispose()
        {
            Id.Value = _previousId;
            Knowledge.Value = _previousKnowledge;
            Progress.Value = _previousProgress;
        }
    }
}
