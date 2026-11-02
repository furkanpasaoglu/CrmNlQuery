using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Ask.Steps;

/// <summary>
/// Resolves ties in ranked producer steps so that the model does not silently pick one of several equal records.
/// </summary>
public static class TieHandling
{
    public const int MaxRows = StepInputFiller.MaxIds + 1;

    /// <summary>
    /// Describes how a tie should be handled for a ranked step.
    /// </summary>
    public sealed record TiePlan(string FetchXml, int N, string MeasureColumn);

    /// <summary>
    /// Describes the outcome of resolving ties in a result.
    /// </summary>
    public sealed record TieOutcome(ResultTable Table, string? Note);

    /// <summary>
    /// Prepares a ranked step for tie handling; when assumeTopOne is set, a ranking without top is treated as N=1.
    /// </summary>
    public static TiePlan? Prepare(string fetchXml, bool assumeTopOne = false)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            return null;
        }
        var root = doc.Root;
        var entity = root?.Element("entity");
        if (root is null || entity is null)
        {
            return null;
        }
        var n = 0;
        var hasTop = int.TryParse((string?)root.Attribute("top"), out n);
        if (assumeTopOne && (!hasTop || n >= MaxRows))
        {
            n = 1;
        }
        else if (!hasTop || n < 1 || n >= MaxRows)
        {
            return null;
        }
        var aggregate = string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase);
        var order = aggregate ? entity.Descendants("order").FirstOrDefault() : entity.Elements("order").FirstOrDefault();
        if (order is null)
        {
            return null;
        }

        string measure;
        if (aggregate)
        {
            var alias = (string?)order.Attribute("alias");
            if (string.IsNullOrEmpty(alias))
            {
                return null;
            }
            measure = alias;
        }
        else
        {
            var orderedAttribute = (string?)order.Attribute("attribute");
            if (string.IsNullOrEmpty(orderedAttribute))
            {
                return null;
            }
            var selected = entity.Elements("attribute").FirstOrDefault(a => string.Equals((string?)a.Attribute("name"), orderedAttribute, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                var last = entity.Elements("attribute").LastOrDefault();
                var added = new XElement("attribute", new XAttribute("name", orderedAttribute));
                if (last is null)
                {
                    entity.AddFirst(added);
                }
                else
                {
                    last.AddAfterSelf(added);
                }
                measure = orderedAttribute;
            }
            else
            {
                measure = (string?)selected.Attribute("alias") ?? orderedAttribute;
            }
        }

        root.SetAttributeValue("top", MaxRows);
        return new TiePlan(doc.ToString(SaveOptions.DisableFormatting), n, measure);
    }

    /// <summary>
    /// Applies tie handling to a result table, keeping every record tied at the boundary and noting it.
    /// </summary>
    public static TieOutcome Resolve(TiePlan plan, ResultTable table)
    {
        var column = table.Columns.ToList().FindIndex(c => string.Equals(c, plan.MeasureColumn, StringComparison.OrdinalIgnoreCase)
            || c.EndsWith("." + plan.MeasureColumn, StringComparison.OrdinalIgnoreCase));
        if (column < 0 || table.Rows.Count <= plan.N)
        {
            return new TieOutcome(table, null);
        }

        var boundary = table.Rows[plan.N - 1][column];
        var kept = plan.N;
        while (kept < table.Rows.Count && table.Rows[kept][column] == boundary)
        {
            kept++;
        }
        if (kept == table.Rows.Count)
        {
            return new TieOutcome(table, kept > plan.N ? TieNote(kept, boundary, plan.MeasureColumn) : null);
        }

        var trimmed = new ResultTable(table.Columns, table.Rows.Take(kept).ToList(), table.RowIds?.Take(kept).ToList());
        return new TieOutcome(trimmed, kept > plan.N ? TieNote(kept, boundary, plan.MeasureColumn) : null);
    }

    private static string TieNote(int kept, string value, string measure)
    {
        return CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.TieNote, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("kept", kept.ToString()), ("value", value), ("measure", measure)));
    }
}
