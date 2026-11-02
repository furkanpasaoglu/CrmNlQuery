namespace CrmNlQuery.Application.Operations.Models;

/// <summary>
/// Describes why a question was denied: a fixed kind for the dashboard, a user message, retry delay and HTTP status.
/// </summary>
public sealed record GateDenial(string Kind, string Message, int RetryAfterSeconds, int HttpStatus)
{
    public const string AsksPaused = "asks_paused";
    public const string RateLimited = "rate_limited";
    public const string DailyQuota = "daily_quota";
    public const string BusyUser = "busy_user";
    public const string BusyTotal = "busy_total";
}
