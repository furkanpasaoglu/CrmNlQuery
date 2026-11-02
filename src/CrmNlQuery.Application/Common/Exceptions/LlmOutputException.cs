namespace CrmNlQuery.Application.Common.Exceptions;

/// <summary>
/// Exception raised when the model answered but not in the expected JSON schema.
/// </summary>
public sealed class LlmOutputException : Exception
{
    public LlmOutputException(string message) : base(message) { }
}
