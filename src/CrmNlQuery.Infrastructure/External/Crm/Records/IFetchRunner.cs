using System.Text.Json;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// Defines the raw CRM Web API query runner that returns the value array as JSON, for infrastructure that needs raw data.
/// </summary>
public interface IFetchRunner
{
    /// <summary>
    /// Runs a FetchXML query and returns the Web API value array as an independent JSON copy.
    /// </summary>
    Task<JsonElement> RunAsync(string entitySetName, string fetchXml, Caller caller, CancellationToken ct = default);
}
