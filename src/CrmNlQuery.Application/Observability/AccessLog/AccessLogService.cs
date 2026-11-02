using CrmNlQuery.Application.Observability.Abstractions;

namespace CrmNlQuery.Application.Observability.AccessLog;

/// <summary>
/// Application service implementing the access log rules.
/// </summary>
public sealed class AccessLogService
{
    private readonly IAccessLogStore _store;
    private readonly IUserNames _names;

    public AccessLogService(IAccessLogStore store, IUserNames names)
    {
        _store = store;
        _names = names;
    }

    /// <summary>
    /// Lists access-log rows for the query and resolves user names from the CRM.
    /// </summary>
    public async Task<IReadOnlyList<AccessLogRow>> ListAsync(DataAccessQuery query, CancellationToken ct = default)
    {
        var records = await _store.QueryAsync(query, ct);
        IReadOnlyDictionary<Guid, string> names = new Dictionary<Guid, string>();
        if (records.Count > 0)
        {
            try
            {
                names = await _names.GetNamesAsync(records.Select(r => r.User).Distinct().ToList(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
        return records.Select(r => new AccessLogRow(r.Id, r.At, r.User, names.TryGetValue(r.User, out var name) ? name : null,
            r.Source, r.Tables, r.Rows, r.Outcome, r.RequestId)).ToList();
    }
}
