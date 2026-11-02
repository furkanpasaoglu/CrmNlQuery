using System.Text.Json;
using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Model-free parser that reads pasted JSON in several shapes (single object, array, entity wrapper) into records for later validation.
/// </summary>
public static class CreateJsonInput
{
    private static readonly string[] EntityKeys = { "entity", "table", "type", "@entity" };
    private static readonly string[] ListKeys = { "records", "items", "data", "rows" };

    /// <summary>
    /// Tries to parse for create json input.
    /// </summary>
    public static CreateJsonResult TryParse(string text, IReadOnlyList<CreateSchema> writable, out CreateJson? json)
    {
        json = null;
        var raw = Extract(text);
        if (raw is null)
        {
            return CreateJsonResult.NotJson;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return CreateJsonResult.NotJson;
        }
        using (document)
        {
            var root = document.RootElement;
            string? entity = null;
            var records = new List<IReadOnlyDictionary<string, string>>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                return Collect(root, records, null, out json);
            }
            if (root.ValueKind != JsonValueKind.Object)
            {
                return CreateJsonResult.NotJson;
            }

            foreach (var key in EntityKeys)
            {
                if (TryGetString(root, key, out var named))
                {
                    entity = named;
                }
            }
            foreach (var key in ListKeys)
            {
                if (root.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    return Collect(list, records, entity, out json);
                }
            }
            foreach (var property in root.EnumerateObject())
            {
                var schema = writable.FirstOrDefault(s => Matches(s, property.Name));
                if (schema is null)
                {
                    continue;
                }
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return Collect(property.Value, records, schema.Entity, out json);
                }
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    return Collect(JsonDocument.Parse("[" + property.Value.GetRawText() + "]").RootElement.Clone(), records, schema.Entity, out json);
                }
            }
            var single = ToRecord(root, EntityKeys);
            if (single is null)
            {
                return CreateJsonResult.Unsupported;
            }
            records.Add(single);
            json = new CreateJson(entity, records);
            return CreateJsonResult.Ok;
        }
    }

    private static CreateJsonResult Collect(JsonElement list, List<IReadOnlyDictionary<string, string>> records, string? entity, out CreateJson? json)
    {
        json = null;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return CreateJsonResult.Unsupported;
            }
            var record = ToRecord(item, EntityKeys);
            if (record is null)
            {
                return CreateJsonResult.Unsupported;
            }
            records.Add(record);
        }
        if (records.Count == 0)
        {
            return CreateJsonResult.Unsupported;
        }
        json = new CreateJson(entity, records);
        return CreateJsonResult.Ok;
    }

    private static IReadOnlyDictionary<string, string>? ToRecord(JsonElement obj, string[] skipKeys)
    {
        var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.EnumerateObject())
        {
            if (skipKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    record[property.Name] = property.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                    record[property.Name] = property.Value.GetRawText();
                    break;
                case JsonValueKind.True:
                    record[property.Name] = "true";
                    break;
                case JsonValueKind.False:
                    record[property.Name] = "false";
                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    return null;
            }
        }
        return record;
    }

    private static bool TryGetString(JsonElement obj, string key, out string? value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString();
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Handles matches for create json input.
    /// </summary>
    internal static bool Matches(CreateSchema schema, string key)
    {
        var wanted = CreateSchema.Key(key);
        foreach (var name in new[] { schema.Entity, schema.Label, schema.EntitySet })
        {
            if (CreateSchema.Key(name) == wanted)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Extracts the first JSON fragment from text, ignoring code fences and surrounding sentences; returns null if none is found.
    /// </summary>
    internal static string? Extract(string text)
    {
        var start = text.IndexOfAny(new[] { '{', '[' });
        if (start < 0)
        {
            return null;
        }
        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var end = text.LastIndexOf(close);
        return end > start ? text.Substring(start, end - start + 1) : null;
    }
}
