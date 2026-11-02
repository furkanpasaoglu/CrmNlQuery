namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// Describes the step trigger type: none, ranking only or always.
/// </summary>
public enum StepTrigger
{
    None,
    OnlyRanking,
    Always,
}
