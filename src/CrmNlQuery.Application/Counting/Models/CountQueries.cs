namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Holds the count queries for a stored query: aggregate FetchXML, total-count fallback and entity set name.
/// </summary>
public sealed record CountQueries(string EntitySetName, string AggregateFetchXml, string TotalCountFetchXml, string EntityLabel = "");
