using System.Globalization;
using System.Text.RegularExpressions;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Converts user or model text into the schema's field types and checks required fields, without writing to the CRM.
/// </summary>
public sealed class CreateValidator
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ",
    };

    private static readonly HashSet<string> Yes = new HashSet<string>(StringComparer.Ordinal) { "evet", "e", "yes", "y", "true", "1", "dogru", "var" };
    private static readonly HashSet<string> No = new HashSet<string>(StringComparer.Ordinal) { "hayir", "h", "no", "n", "false", "0", "yanlis", "yok" };

    private readonly LookupSearch _lookup;

    public CreateValidator(LookupSearch lookup)
    {
        _lookup = lookup;
    }

    /// <summary>
    /// Validates the supplied field texts against the schema and returns the typed row with any problems.
    /// </summary>
    public async Task<CreateRow> ValidateAsync(CreateSchema schema, IReadOnlyDictionary<string, string> raw, CancellationToken ct = default)
    {
        var problems = new List<CreateProblem>();
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in raw)
        {
            var field = schema.FindField(pair.Key);
            if (field is null)
            {
                problems.Add(new CreateProblem(pair.Key, pair.Key, "unknown_field"));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                canonical[field.Name] = pair.Value.Trim();
            }
        }

        var values = new List<CreateValue>();
        foreach (var field in schema.Fields)
        {
            if (!canonical.TryGetValue(field.Name, out var text))
            {
                if (field.Required)
                {
                    problems.Add(new CreateProblem(field.Name, field.Label, "missing_required"));
                }
                continue;
            }
            var (value, problem) = await ConvertAsync(field, text, ct);
            if (problem is not null)
            {
                problems.Add(problem);
            }
            else if (value is not null)
            {
                values.Add(value);
            }
        }

        var name = values.FirstOrDefault(v => v.Attribute == schema.PrimaryNameAttribute)?.Display ?? "";
        if (name.Length == 0 && schema.FindField(schema.PrimaryNameAttribute) is null)
        {
            var first = values.FirstOrDefault(v => v.Attribute == "firstname")?.Display ?? "";
            var last = values.FirstOrDefault(v => v.Attribute == "lastname")?.Display ?? "";
            name = (first + " " + last).Trim();
        }
        return new CreateRow(canonical, values, problems, name);
    }

    private async Task<(CreateValue? Value, CreateProblem? Problem)> ConvertAsync(CreateField field, string text, CancellationToken ct)
    {
        CreateProblem Fail(string code, string detail = "") => new CreateProblem(field.Name, field.Label, code, detail);
        switch (field.Type)
        {
            case CreateFieldType.Text:
            case CreateFieldType.LongText:
                if (field.MaxLength is int max && text.Length > max)
                {
                    return (null, Fail("too_long", max.ToString(CultureInfo.InvariantCulture)));
                }
                return (new CreateValue(field.Name, field.Label, text, text), null);

            case CreateFieldType.WholeNumber:
                return TryWhole(text, out var whole)
                    ? (new CreateValue(field.Name, field.Label, whole.ToString(CultureInfo.InvariantCulture), whole), null)
                    : (null, Fail("invalid_number"));

            case CreateFieldType.Decimal:
                return TryDecimal(text, out var number)
                    ? (new CreateValue(field.Name, field.Label, number.ToString(CultureInfo.InvariantCulture), number), null)
                    : (null, Fail("invalid_number"));

            case CreateFieldType.Boolean:
            {
                var key = CreateSchema.Key(text);
                if (Yes.Contains(key)) { return (new CreateValue(field.Name, field.Label, "✓", true), null); }
                if (No.Contains(key)) { return (new CreateValue(field.Name, field.Label, "✗", false), null); }
                return (null, Fail("invalid_boolean"));
            }

            case CreateFieldType.Date:
                if (DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                {
                    return (new CreateValue(field.Name, field.Label, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture)), null);
                }
                return (null, Fail("invalid_date"));

            case CreateFieldType.Choice:
            {
                var key = CreateSchema.Key(text);
                var option = field.Options.FirstOrDefault(o => CreateSchema.Key(o.Label) == key)
                    ?? (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? field.Options.FirstOrDefault(o => o.Value == code) : null);
                return option is null
                    ? (null, Fail("choice_not_found", string.Join(", ", field.Options.Take(12).Select(o => o.Label))))
                    : (new CreateValue(field.Name, field.Label, option.Label, option.Value), null);
            }

            case CreateFieldType.Lookup:
                return await ResolveLookupAsync(field, text, ct);

            default:
                return (null, Fail("unknown_field"));
        }
    }

    private async Task<(CreateValue? Value, CreateProblem? Problem)> ResolveLookupAsync(CreateField field, string text, CancellationToken ct)
    {
        var found = new List<(LookupTarget Target, RecordMatch Match)>();
        foreach (var target in field.Targets)
        {
            foreach (var match in await _lookup(target.Entity, text, ct))
            {
                found.Add((target, match));
            }
        }
        if (found.Count == 0)
        {
            return (null, new CreateProblem(field.Name, field.Label, "lookup_not_found", text));
        }
        if (found.Count > 1)
        {
            return (null, new CreateProblem(field.Name, field.Label, "lookup_ambiguous", string.Join(", ", found.Take(3).Select(f => f.Match.Name))));
        }
        var (hit, record) = found[0];
        return (new CreateValue(field.Name, field.Label, record.Name, null, hit.NavigationProperty + "@odata.bind", "/" + hit.EntitySet + "(" + record.Id.ToString("D") + ")"), null);
    }

    private static bool TryWhole(string text, out long value)
    {
        var s = text.Replace(" ", "").Replace(" ", "");
        if (Regex.IsMatch(s, @"^[+-]?\d{1,3}([.,]\d{3})+$"))
        {
            s = s.Replace(".", "").Replace(",", "");
        }
        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDecimal(string text, out decimal value)
    {
        var s = text.Replace(" ", "").Replace(" ", "");
        var commas = s.Count(c => c == ',');
        var dots = s.Count(c => c == '.');
        if (commas > 0 && dots > 0)
        {
            s = s.LastIndexOf(',') > s.LastIndexOf('.') ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        }
        else if (commas > 0)
        {
            s = commas == 1 ? s.Replace(',', '.') : s.Replace(",", "");
        }
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
