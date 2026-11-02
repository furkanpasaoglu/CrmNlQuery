using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Domain.Access.Entities;
namespace CrmNlQuery.Application.Counting.Abstractions;

/// <summary>
/// Counts records in the CRM in two tiers, both with the caller's permissions, throwing a CRM exception on failure.
/// </summary>
public interface IRecordCounter
{
    /// <summary>
    /// Counts with an aggregate countcolumn distinct query, exact up to the CRM aggregate limit.
    /// </summary>
    Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default);

    /// <summary>
    /// Counts with returntotalrecordcount, returning the exact count or what the CRM could count when the limit was exceeded.
    /// </summary>
    Task<RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default);
}
