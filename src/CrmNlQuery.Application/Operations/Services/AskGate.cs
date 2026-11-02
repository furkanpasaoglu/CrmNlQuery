using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Decides before a question reaches the model or CRM whether it is paused, too concurrent, over minute or daily quota or the server is full, reading limits on every call.
/// </summary>
public sealed class AskGate
{
    public const int DefaultMaxUsers = 10000;

    private readonly ILimitsSource _limits;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxUsers;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, UserState> _users = new Dictionary<Guid, UserState>();
    private int _running;

    public AskGate(ILimitsSource limits, Func<DateTimeOffset>? clock = null, int maxUsers = DefaultMaxUsers)
    {
        _limits = limits;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxUsers = Math.Max(1, maxUsers);
    }

    private sealed class UserState
    {
        public readonly Queue<DateTimeOffset> Recent = new Queue<DateTimeOffset>();
        public DateOnly Day;
        public int DayCount;
        public int Running;
    }

    public int RunningTotal
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public int TrackedUsers
    {
        get
        {
            lock (_gate)
            {
                return _users.Count;
            }
        }
    }

    /// <summary>
    /// Checks a user's question against pause, quotas and concurrency and returns an allow decision with a lease or a denial.
    /// </summary>
    public GateDecision TryEnter(Guid user)
    {
        var limits = _limits.Current;
        var now = _clock();
        lock (_gate)
        {
            if (!limits.AskEnabled)
            {
                return Deny(GateDenial.AsksPaused, MessageCatalog.Now(Msg.GateAsksPaused), 60, 503);
            }

            Trim(now);
            if (!_users.TryGetValue(user, out var state))
            {
                state = new UserState { Day = DateOnly.FromDateTime(now.UtcDateTime) };
                _users[user] = state;
            }
            while (state.Recent.Count > 0 && now - state.Recent.Peek() >= TimeSpan.FromSeconds(60))
            {
                state.Recent.Dequeue();
            }
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (state.Day != today)
            {
                state.Day = today;
                state.DayCount = 0;
            }

            if (state.Running >= limits.MaxConcurrentPerUser)
            {
                return Deny(GateDenial.BusyUser, MessageCatalog.Now(Msg.GateBusyUser, MessageCatalog.Args(("running", state.Running.ToString()))), 5, 429);
            }
            if (state.Recent.Count >= limits.QuestionsPerMinute)
            {
                var wait = (int)Math.Ceiling((TimeSpan.FromSeconds(60) - (now - state.Recent.Peek())).TotalSeconds);
                return Deny(GateDenial.RateLimited, MessageCatalog.Now(Msg.GateRateLimited, MessageCatalog.Args(("limit", limits.QuestionsPerMinute.ToString()))), Math.Max(1, wait), 429);
            }
            if (state.DayCount >= limits.QuestionsPerDay)
            {
                var midnight = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return Deny(GateDenial.DailyQuota, MessageCatalog.Now(Msg.GateDailyQuota, MessageCatalog.Args(("limit", limits.QuestionsPerDay.ToString()))),
                    Math.Max(1, (int)Math.Ceiling((midnight - now).TotalSeconds)), 429);
            }
            if (_running >= limits.MaxConcurrentTotal)
            {
                return Deny(GateDenial.BusyTotal, MessageCatalog.Now(Msg.GateBusyTotal), 10, 503);
            }

            state.Recent.Enqueue(now);
            state.DayCount++;
            state.Running++;
            _running++;
            return GateDecision.Allow(new GateLease(() => Release(user)));
        }
    }

    private static GateDecision Deny(string kind, string message, int retryAfterSeconds, int status)
        => GateDecision.Deny(new GateDenial(kind, message, retryAfterSeconds, status));

    private void Release(Guid user)
    {
        lock (_gate)
        {
            _running = Math.Max(0, _running - 1);
            if (_users.TryGetValue(user, out var state))
            {
                state.Running = Math.Max(0, state.Running - 1);
            }
        }
    }

    private void Trim(DateTimeOffset now)
    {
        if (_users.Count < _maxUsers)
        {
            return;
        }
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var idle = _users
            .Where(p => p.Value.Running == 0 && p.Value.Day != today
                && (p.Value.Recent.Count == 0 || now - p.Value.Recent.Peek() >= TimeSpan.FromSeconds(60)))
            .Select(p => p.Key)
            .ToList();
        foreach (var key in idle)
        {
            _users.Remove(key);
        }
    }
}
