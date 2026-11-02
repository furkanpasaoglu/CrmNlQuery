namespace CrmNlQuery.Application.Ask.Planning;

/// <summary>
/// Validation context of one step in a dependent plan: which earlier step it uses, which entities earlier steps produced ids for, and whether it produces ids.
/// </summary>
public sealed record StepContext(int? UsesStep, IReadOnlyDictionary<int, IReadOnlyCollection<string>> PreviousIdEntities, bool ProducesIds = false);
