namespace CrmNlQuery.Domain.Ask.Entities;

/// <summary>
/// Holds the previous answer's query in the same conversation: question, entity and validated FetchXML, never row data.
/// </summary>
public sealed record PreviousQuery(string Question, string Entity, string FetchXml);
