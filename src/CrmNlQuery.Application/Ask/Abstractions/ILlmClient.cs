namespace CrmNlQuery.Application.Ask.Abstractions;

/// <summary>
/// Port for a language model client that returns structured JSON output.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Calls the model with a JSON schema that constrains the output and returns the raw JSON text.
    /// </summary>
    Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default);

    /// <summary>
    /// Calls the model like the JSON call but also returns token usage; the default returns text only.
    /// </summary>
    async Task<LlmResult> CompleteAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
    {
        return new LlmResult(await CompleteJsonAsync(system, user, jsonSchema, ct), null);
    }
}
