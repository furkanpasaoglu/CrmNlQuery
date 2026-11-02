namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Supplies company knowledge pieces relevant to a question.
/// </summary>
public interface IKnowledgeSource
{
    /// <summary>
    /// Finds knowledge pieces for a question.
    /// </summary>
    Task<IReadOnlyList<string>> FindAsync(string question, CancellationToken ct = default);
}
