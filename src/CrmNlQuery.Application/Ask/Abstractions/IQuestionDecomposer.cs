using CrmNlQuery.Domain.Ask.Steps;

namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Port that splits a question into sequential steps.
/// </summary>
public interface IQuestionDecomposer
{
    /// <summary>
    /// Splits a question into one to three sequential steps, the first always unconditional.
    /// </summary>
    Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default);
}
