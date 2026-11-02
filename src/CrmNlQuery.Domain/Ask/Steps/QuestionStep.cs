namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// One step of a decomposed question with its text, condition and the earlier step whose ids it uses.
/// </summary>
public sealed record QuestionStep(string Question, StepCondition Condition, int? UsesStep = null);
