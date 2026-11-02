namespace CrmNlQuery.Domain.Access.Entities;

/// <summary>
/// An administrator's explicit decision to open or close a table or field.
/// </summary>
public sealed record DataPolicyRule(string Kind, string Entity, string? Attribute, bool Allowed, string SetBy, DateTimeOffset SetAt)
{
    public const string Table = "table";
    public const string Field = "field";
}
