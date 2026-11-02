using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Looks up a table's CRM object type code, needed for the report viewer's records context, using the service account for metadata.
/// </summary>
public sealed class CrmObjectTypeCodes : IObjectTypeCodeSource
{
    private readonly CrmClient _client;
    private readonly Dictionary<string, int> _codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    public CrmObjectTypeCodes(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Returns the object type code of an entity, or null if unknown.
    /// </summary>
    public async Task<int?> GetAsync(string entityLogicalName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_codes.TryGetValue(entityLogicalName, out var known)) { return known; }
            try
            {
                using var doc = await _client.GetAsServiceAsync($"EntityDefinitions(LogicalName='{entityLogicalName.Replace("'", "''")}')?$select=ObjectTypeCode", ct);
                var code = doc.RootElement.GetProperty("ObjectTypeCode").GetInt32();
                _codes[entityLogicalName] = code;
                return code;
            }
            catch (Exception ex) when (ex is CrmException or HttpRequestException or KeyNotFoundException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
