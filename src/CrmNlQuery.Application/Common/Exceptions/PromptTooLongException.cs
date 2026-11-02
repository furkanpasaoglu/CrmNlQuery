namespace CrmNlQuery.Application.Common.Exceptions;

/// <summary>
/// Exception raised when the model context window is full, either the prompt did not fit or thinking and output filled it.
/// </summary>
public sealed class PromptTooLongException : Exception
{
    public const string PromptCause = "prompt";
    public const string GenerationCause = "generation";

    public PromptTooLongException(string cause, int promptTokens, int outputTokens, int contextTokens)
        : base($"Model context window is full ({cause}): prompt {promptTokens} tokens, output/thinking {outputTokens} tokens, window {contextTokens}.")
    {
        Cause = cause;
        PromptTokens = promptTokens;
        OutputTokens = outputTokens;
        ContextTokens = contextTokens;
    }

    public string Cause { get; }
    public int PromptTokens { get; }
    public int OutputTokens { get; }
    public int ContextTokens { get; }
}
