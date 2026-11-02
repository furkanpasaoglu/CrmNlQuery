using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Runs a validated query and returns the result as a CRM-independent table.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes the FetchXML as the given CRM user, or as the service account when null.
    /// </summary>
    Task<ResultTable> ExecuteAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default);
}
