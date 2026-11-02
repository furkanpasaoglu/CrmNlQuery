using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Guards the previous query used as follow-up context, dropping it when it uses data that is now closed.
/// </summary>
public static class PreviousQueryGuard
{
    /// <summary>
    /// Returns the previous query unchanged, or null when it contains data closed by the data policy.
    /// </summary>
    public static async Task<PreviousQuery?> FilterAsync(PreviousQuery? previous, IMetadataSource metadata, CancellationToken ct = default)
    {
        if (previous is null)
        {
            return null;
        }
        return await StoredQueryCheck.IsUsableAsync(previous.FetchXml, metadata, ct) ? previous : null;
    }
}
