using CrmNlQuery.Application.Create.Models;

namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Holds the validation result of one record to create, including the raw user-supplied texts.
/// </summary>
public sealed class CreateRow
{
    public CreateRow(IReadOnlyDictionary<string, string> raw, IReadOnlyList<CreateValue> values, IReadOnlyList<CreateProblem> problems, string displayName)
    {
        Raw = raw;
        Values = values;
        Problems = problems;
        DisplayName = displayName;
    }

    public IReadOnlyDictionary<string, string> Raw { get; }

    public IReadOnlyList<CreateValue> Values { get; }

    public IReadOnlyList<CreateProblem> Problems { get; }

    public string DisplayName { get; }

    public bool IsComplete => Problems.Count == 0;

    /// <summary>
    /// Builds the CRM request body from typed values and lookup bindings, meaningful only for a complete record.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ToPayload()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var value in Values)
        {
            if (value.BindName is not null)
            {
                payload[value.BindName] = value.BindValue;
            }
            else
            {
                payload[value.Attribute] = value.Json;
            }
        }
        return payload;
    }
}
