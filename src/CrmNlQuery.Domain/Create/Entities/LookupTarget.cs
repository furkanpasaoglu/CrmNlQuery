namespace CrmNlQuery.Domain.Create.Entities;

/// <summary>
/// Target table of a lookup field, with the navigation property used for binding.
/// </summary>
public sealed record LookupTarget(string Entity, string EntitySet, string NavigationProperty);
