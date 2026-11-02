namespace CrmNlQuery.Domain.Create.Entities;

/// <summary>
/// One selectable option of a writable option-set field.
/// </summary>
public sealed record CreateOption(int Value, string Label);
