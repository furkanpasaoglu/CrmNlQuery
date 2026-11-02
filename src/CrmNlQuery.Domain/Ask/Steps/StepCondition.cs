namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// Defines when a question step runs, based on the previous step's row count.
/// </summary>
public enum StepCondition
{
    Always,
    IfPreviousEmpty,
    IfPreviousNotEmpty,
}
