using System.Text.Json;

namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Data model used by the customization design assistant for json.
/// </summary>
public static class DesignJson
{
    public static readonly JsonSerializerOptions Read = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Write = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}
