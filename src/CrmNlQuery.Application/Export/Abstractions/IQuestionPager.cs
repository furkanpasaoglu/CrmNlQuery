using CrmNlQuery.Application.Export.Models;

namespace CrmNlQuery.Application.Export.Abstractions;

/// <summary>
/// Pages question texts by key for the optional admin export dataset, without loading everything at once.
/// </summary>
public interface IQuestionPager
{
    /// <summary>
    /// Returns one page of questions flagged admin-viewable at ask time, optionally for one user.
    /// </summary>
    Task<IReadOnlyList<QuestionPageItem>> PageQuestionsAsync(DateTimeOffset from, DateTimeOffset to, Guid? user, QuestionCursor? after, int size, CancellationToken ct = default);
}
