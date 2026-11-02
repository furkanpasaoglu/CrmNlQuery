namespace CrmNlQuery.Application.Create.Models;

/// <summary>
/// Holds one validated field value: the typed CRM value, or the lookup binding name and value.
/// </summary>
public sealed record CreateValue(string Attribute, string Label, string Display, object? Json, string? BindName = null, string? BindValue = null);
