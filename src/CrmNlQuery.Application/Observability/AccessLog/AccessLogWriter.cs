using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Internal writer that records access-log rows, skips the evaluation caller, and never lets a store failure break the calling operation.
/// </summary>
internal sealed class AccessLogWriter
{
    private readonly IAccessLogStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public AccessLogWriter(IAccessLogStore store, Func<DateTimeOffset>? clock)
    {
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Writes one access-log row for the caller with tables, row count, outcome and optional source override.
    /// </summary>
    public async Task WriteAsync(Caller caller, IReadOnlyList<string> tables, int rows, string outcome, string? sourceOverride = null)
    {
        if (caller.IsEvaluation)
        {
            return;
        }
        try
        {
            var requestId = AccessLogContext.RequestId ?? Guid.NewGuid();
            await _store.AddAsync(new DataAccessRecord(Guid.NewGuid(), _clock(), caller.UserId, sourceOverride ?? AccessLogContext.Source, tables, rows, outcome, requestId));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Handles outcome of for access log writer.
    /// </summary>
    public static string OutcomeOf(Exception ex) => ex is OperationCanceledException ? "cancelled" : ex is CrmException { IsPermissionDenied: true } ? "crm_denied" : "error";
}
