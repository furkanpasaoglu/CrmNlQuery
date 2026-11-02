using CrmNlQuery.Application.Observability.Metrics;
using CrmNlQuery.Application.Observability.Abstractions;

namespace CrmNlQuery.Application.Observability.Services;

/// <summary>
/// Builds the admin panel report for the last N days: usage, top users and health, using UTC day boundaries.
/// </summary>
public sealed class AdminService
{
    public const int MaxDays = 90;

    private readonly IMetricsStore _store;
    private readonly IUserNames _names;
    private readonly IReadOnlyDictionary<string, ModelPrice>? _prices;
    private readonly Func<DateTimeOffset> _clock;

    public AdminService(IMetricsStore store, IUserNames names, IReadOnlyDictionary<string, ModelPrice>? prices, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _names = names;
        _prices = prices;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Builds the admin report for the last given number of days.
    /// </summary>
    public async Task<AdminReport> ReportAsync(int days, CancellationToken ct = default)
    {
        var count = Math.Clamp(days, 1, MaxDays);
        var now = _clock().ToUniversalTime();
        var to = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var from = to.AddDays(-count);

        var summary = await _store.GetSummaryAsync(from, to, ct);

        IReadOnlyDictionary<Guid, string> names = new Dictionary<Guid, string>();
        try
        {
            names = await _names.GetNamesAsync(summary.TopUsers.Select(u => u.User), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return AdminReportBuilder.Build(summary, from, to, _prices, names);
    }
}
