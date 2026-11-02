using System.Text.Json;

namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Stored email draft: placeholder template and tone, never the customer's values.
/// </summary>
public sealed record DraftPayload(string Subject, string Body, string Tone)
{
    /// <summary>
    /// Serializes the draft payload to JSON.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Parses a stored draft payload from JSON, returning null when it is empty or corrupt.
    /// </summary>
    public static DraftPayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var payload = JsonSerializer.Deserialize<DraftPayload>(json);
            return payload is null || string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Body) ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
