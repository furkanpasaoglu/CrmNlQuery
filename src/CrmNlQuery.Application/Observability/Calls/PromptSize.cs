namespace CrmNlQuery.Application.Observability.Calls;

/// <summary>
/// Provides a rough token estimate for prompt size by dividing characters by 3.5, since real token counts are recorded separately.
/// </summary>
public static class PromptSize
{
    public const double CharsPerToken = 3.5;

    /// <summary>
    /// Counts the total characters of a prompt's system text, user text and JSON schema.
    /// </summary>
    public static int Chars(string system, string user, string jsonSchema) => system.Length + user.Length + jsonSchema.Length;

    /// <summary>
    /// Estimates tokens from a character count.
    /// </summary>
    public static int EstimateTokens(int chars) => (int)Math.Round(chars / CharsPerToken);
}
