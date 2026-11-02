using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.DataPolicy.Abstractions;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Provides a cached snapshot of the data allowlist (30 seconds by default), keeping the previous snapshot if the store fails and reading the main switch every call.
/// </summary>
public sealed class DataPolicyService : IDataPolicy
{
    private sealed record State(DataPolicySnapshot Snapshot, DateTimeOffset LoadedAt);

    private readonly IDataPolicyStore _store;
    private readonly Func<IReadOnlyCollection<string>> _defaultTables;
    private readonly Func<bool> _enabled;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _ttl;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private volatile State? _state;
    private int _generation;

    public DataPolicyService(IDataPolicyStore store, Func<IReadOnlyCollection<string>> defaultTables, Func<bool> enabled,
        Func<DateTimeOffset>? clock = null, TimeSpan? ttl = null, ILogger? logger = null)
    {
        _store = store;
        _defaultTables = defaultTables;
        _enabled = enabled;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
        _logger = logger ?? NullLogger.Instance;
    }

    public bool Persistent => _store.Persistent;

    /// <summary>
    /// Drops the cached snapshot so a panel change applies immediately on this server.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        var current = _state;
        if (current is not null)
        {
            _state = current with { LoadedAt = DateTimeOffset.MinValue };
        }
    }

    /// <summary>
    /// Returns the cached policy snapshot, reloading it from the store when expired or invalidated.
    /// </summary>
    public async Task<DataPolicySnapshot> GetAsync(CancellationToken ct = default)
    {
        var state = _state;
        if (state is null || _clock() - state.LoadedAt >= _ttl)
        {
            await _gate.WaitAsync(ct);
            try
            {
                state = _state;
                if (state is null || _clock() - state.LoadedAt >= _ttl)
                {
                    state = await ReloadAsync(state, ct);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        return state.Snapshot.WithEnabled(_enabled());
    }

    private async Task<State> ReloadAsync(State? previous, CancellationToken ct)
    {
        var generation = Volatile.Read(ref _generation);
        try
        {
            var rules = await _store.ListAsync(ct);
            var snapshot = new DataPolicySnapshot(rules, _defaultTables(), true);
            var stale = generation != Volatile.Read(ref _generation);
            _state = new State(snapshot, stale ? DateTimeOffset.MinValue : _clock());
            return _state;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (previous is null)
            {
                _logger.LogError(ex, "The data policy could not be read and there is no earlier snapshot: questions are refused until it can be read.");
                throw;
            }
            _logger.LogWarning(ex, "The data policy could not be read; the previous snapshot is kept");
            _state = previous with { LoadedAt = _clock() };
            return _state;
        }
    }
}
