using System.Globalization;
using System.Xml.Linq;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Application.Glossary.Services;

/// <summary>
/// Checks in code whether the model applied a glossary definition to the query, never trusting the model's claim.
/// </summary>
public static class GlossaryEnforcer
{
    public const string Marker = "glossary term";

    /// <summary>
    /// Evaluates a FetchXML against the glossary hits and returns the findings where a definition was not applied.
    /// </summary>
    public static IReadOnlyList<GlossaryFinding> Evaluate(string fetchXml, IReadOnlyList<GlossaryHit> hits)
    {
        var findings = new List<GlossaryFinding>();
        if (hits.Count == 0) { return findings; }
        XDocument doc;
        try { doc = XDocument.Parse(fetchXml); }
        catch (System.Xml.XmlException) { return findings; }

        foreach (var hit in hits)
        {
            var entry = hit.Entry;
            var scopes = Scopes(doc, entry.Entity);
            if (scopes.Count == 0) { continue; }
            if (entry.Kind != GlossaryKind.Filter)
            {
                findings.Add(new GlossaryFinding(hit, true, null));
                continue;
            }
            var satisfied = scopes.Any(scope => entry.Conditions.All(c => Has(scope, c)));
            findings.Add(new GlossaryFinding(hit, satisfied, satisfied ? null :
                $"The {Marker} '{hit.Phrase}' requires these conditions on {entry.Entity}: {GlossaryDescribe.Conditions(entry)}. " +
                $"Add exactly these conditions in an AND filter of the {entry.Entity} entity (or its link-entity) and keep the rest of the query."));
        }
        return findings;
    }

    private static List<XElement> Scopes(XDocument doc, string entity)
    {
        var scopes = new List<XElement>();
        var root = doc.Root?.Element("entity");
        if (root is not null && string.Equals((string?)root.Attribute("name"), entity, StringComparison.OrdinalIgnoreCase)) { scopes.Add(root); }
        if (root is not null)
        {
            scopes.AddRange(root.Descendants("link-entity").Where(l => string.Equals((string?)l.Attribute("name"), entity, StringComparison.OrdinalIgnoreCase)));
        }
        return scopes;
    }

    private static bool Has(XElement scope, GlossaryCondition wanted)
    {
        foreach (var condition in scope.Descendants("condition"))
        {
            if (!string.Equals((string?)condition.Attribute("attribute"), wanted.Attribute, StringComparison.OrdinalIgnoreCase)) { continue; }
            if (!string.Equals((string?)condition.Attribute("operator"), wanted.Operator, StringComparison.OrdinalIgnoreCase)) { continue; }
            if (!InAndOnly(condition, scope)) { continue; }
            if (SameValues(Values(condition), wanted.Values, wanted.Operator)) { return true; }
        }
        return false;
    }

    private static bool InAndOnly(XElement condition, XElement scope)
    {
        for (var node = condition.Parent; node is not null && node != scope; node = node.Parent)
        {
            if (node.Name == "link-entity") { return false; }
            if (node.Name == "filter" && string.Equals((string?)node.Attribute("type"), "or", StringComparison.OrdinalIgnoreCase)) { return false; }
        }
        return true;
    }

    private static List<string> Values(XElement condition)
    {
        var values = condition.Elements("value").Select(v => v.Value.Trim()).ToList();
        var single = (string?)condition.Attribute("value");
        if (single is not null) { values.Add(single.Trim()); }
        return values;
    }

    private static bool SameValues(List<string> actual, IReadOnlyList<string> wanted, string op)
    {
        if (GlossaryOperator.NeedsNoValue(op)) { return true; }
        if (actual.Count != wanted.Count) { return false; }
        var a = actual.Select(v => Norm(v, op)).OrderBy(v => v, StringComparer.Ordinal).ToList();
        var w = wanted.Select(v => Norm(v, op)).OrderBy(v => v, StringComparer.Ordinal).ToList();
        return a.SequenceEqual(w);
    }

    private static string Norm(string value, string op)
    {
        var text = value.Trim();
        if (op == GlossaryOperator.Like) { text = text.Trim('%'); }
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { return number.ToString("0.########", CultureInfo.InvariantCulture); }
        return text.ToLowerInvariant();
    }
}
