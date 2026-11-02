using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Application.Ask.Planning.Validation;

namespace CrmNlQuery.Application.Ask.Steps;

/// <summary>
/// Fills {{stepN}} placeholders in a dependent step with the real ids from earlier steps in code, so the model never sees ids; stops the chain on zero or too many ids.
/// </summary>
public sealed class StepInputFiller
{
    public const int MaxIds = 100;

    private readonly PlanValidator _validator;

    public StepInputFiller(PlanValidator validator)
    {
        _validator = validator;
    }

    /// <summary>
    /// Fills the step placeholders in the FetchXML with deduplicated ids from earlier steps and returns the status and resulting query.
    /// </summary>
    public async Task<FillResult> FillAsync(string fetchXml, IReadOnlyDictionary<int, IReadOnlyList<string>> idsByStep, CancellationToken ct = default)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException ex)
        {
            return new FillResult(FillStatus.Invalid, null, 0, 0, new List<string> { "Invalid XML: " + ex.Message });
        }

        foreach (var condition in doc.Descendants("condition").ToList())
        {
            var placeholder = Placeholder(condition);
            if (placeholder is null)
            {
                continue;
            }

            var step = placeholder.Value;
            var ids = Distinct(idsByStep.TryGetValue(step, out var raw) ? raw : new List<string>());
            if (ids.Count == 0)
            {
                return new FillResult(FillStatus.Empty, null, step, 0, new List<string>());
            }
            if (ids.Count > MaxIds)
            {
                return new FillResult(FillStatus.TooMany, null, step, ids.Count, new List<string>());
            }
            Replace(condition, ids);
        }

        var validation = await _validator.ValidateFetchXmlAsync(doc.ToString(SaveOptions.DisableFormatting), ct);
        return validation.IsValid
            ? FillResult.Filled(validation.NormalizedFetchXml!)
            : new FillResult(FillStatus.Invalid, null, 0, 0, validation.Errors);
    }

    private static int? Placeholder(XElement condition)
    {
        if (StepPlaceholder.TryParse(condition.Attribute("value")?.Value, out var fromAttribute))
        {
            return fromAttribute;
        }
        foreach (var value in condition.Elements("value"))
        {
            if (StepPlaceholder.TryParse(value.Value, out var fromElement))
            {
                return fromElement;
            }
        }
        return null;
    }

    private static List<string> Distinct(IEnumerable<string> raw)
    {
        var seen = new HashSet<System.Guid>();
        var result = new List<string>();
        foreach (var text in raw)
        {
            if (!System.Guid.TryParse(text, out var id))
            {
                throw new InvalidOperationException("A step produced an id that is not a GUID.");
            }
            if (seen.Add(id))
            {
                result.Add(id.ToString());
            }
        }
        return result;
    }

    private static void Replace(XElement condition, List<string> ids)
    {
        condition.Attribute("value")?.Remove();
        condition.Elements("value").ToList().ForEach(v => v.Remove());
        if (ids.Count == 1 && (string?)condition.Attribute("operator") == "eq")
        {
            condition.SetAttributeValue("value", ids[0]);
            return;
        }
        if ((string?)condition.Attribute("operator") == "eq")
        {
            condition.SetAttributeValue("operator", "in");
        }
        foreach (var id in ids)
        {
            condition.Add(new XElement("value", id));
        }
    }
}
