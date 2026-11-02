namespace CrmNlQuery.Application.Common.Exceptions;

/// <summary>
/// Exception thrown when the daily token budget for the cloud model is used up, so the call was not made.
/// </summary>
public sealed class BudgetExceededException : Exception
{
    public BudgetExceededException(string message) : base(message) { }
}
