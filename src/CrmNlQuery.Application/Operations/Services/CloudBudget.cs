namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Tracks the daily UTC token budget for the cloud model, held in memory, seeded at startup and reset at UTC midnight; zero means unlimited.
/// </summary>
public sealed class CloudBudget
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private DateOnly _day;
    private long _tokens;

    public CloudBudget(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _day = Today();
    }

    public long TokensToday
    {
        get
        {
            lock (_gate)
            {
                Roll();
                return _tokens;
            }
        }
    }

    /// <summary>
    /// Seeds the counter with tokens already spent today from metrics at startup.
    /// </summary>
    public void Seed(long tokens)
    {
        lock (_gate)
        {
            Roll();
            _tokens = Math.Max(0, tokens);
        }
    }

    /// <summary>
    /// Adds spent tokens to today's counter.
    /// </summary>
    public void Add(long tokens)
    {
        lock (_gate)
        {
            Roll();
            _tokens += Math.Max(0, tokens);
        }
    }

    /// <summary>
    /// Tells whether today's tokens have reached the given budget.
    /// </summary>
    public bool IsExceeded(long budget) => budget > 0 && TokensToday >= budget;

    private DateOnly Today() => DateOnly.FromDateTime(_clock().UtcDateTime);

    private void Roll()
    {
        var today = Today();
        if (today != _day)
        {
            _day = today;
            _tokens = 0;
        }
    }
}
