namespace CrmNlQuery.Application.Common.Exceptions;

/// <summary>
/// Signals that Ollama could not be reached, timed out or returned an HTTP error.
/// </summary>
public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
