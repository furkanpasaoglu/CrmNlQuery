using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Planning.Prompts;

/// <summary>
/// Builds example FetchXML for the planner from the request's own entities, so absent or unauthorized entities never appear.
/// </summary>
public static class PlanExamples
{
    private const string SampleGuid = "00000000-0000-0000-0000-000000000001";

    /// <summary>
    /// Returns example FetchXML queries built from the given entities.
    /// </summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<EntityInfo> entities)
    {
        var examples = new List<string>();
        if (entities.Count == 0)
        {
            return examples;
        }
        var root = entities[0];
        var rootColumns = Columns(root);

        examples.Add($"<fetch top=\"500\"><entity name=\"{root.LogicalName}\">{rootColumns}</entity></fetch>");

        var link = LinkExample(root, entities);
        if (link is not null)
        {
            examples.Add(link);
        }

        var manyToMany = ManyToManyExample(root, entities);
        if (manyToMany is not null)
        {
            examples.Add(manyToMany);
        }

        examples.Add($"<fetch aggregate=\"true\"><entity name=\"{root.LogicalName}\"><attribute name=\"{root.PrimaryIdAttribute}\" alias=\"count\" aggregate=\"count\" /></entity></fetch>");
        return examples;
    }

    private static string? ManyToManyExample(EntityInfo root, IReadOnlyList<EntityInfo> entities)
    {
        var others = entities.Skip(1).ToDictionary(e => e.LogicalName, StringComparer.OrdinalIgnoreCase);
        foreach (var m in root.ManyToMany.OrderBy(m => m.SchemaName, StringComparer.Ordinal))
        {
            var rootIsFirst = string.Equals(m.Entity1, root.LogicalName, StringComparison.OrdinalIgnoreCase);
            var rootIsSecond = string.Equals(m.Entity2, root.LogicalName, StringComparison.OrdinalIgnoreCase);
            if (!rootIsFirst && !rootIsSecond)
            {
                continue;
            }
            var otherName = rootIsFirst ? m.Entity2 : m.Entity1;
            var rootAttribute = rootIsFirst ? m.Entity1Attribute : m.Entity2Attribute;
            var otherAttribute = rootIsFirst ? m.Entity2Attribute : m.Entity1Attribute;
            if (!others.TryGetValue(otherName, out var other) || string.IsNullOrEmpty(root.PrimaryIdAttribute) || string.IsNullOrEmpty(other.PrimaryIdAttribute)
                || root.FindAttribute(root.PrimaryIdAttribute) is null || other.FindAttribute(other.PrimaryIdAttribute) is null)
            {
                continue;
            }
            return $"<fetch top=\"500\"><entity name=\"{root.LogicalName}\">{Columns(root)}" +
                   $"<link-entity name=\"{m.IntersectEntity}\" from=\"{rootAttribute}\" to=\"{root.PrimaryIdAttribute}\" alias=\"{m.IntersectEntity}\" link-type=\"inner\">" +
                   $"<link-entity name=\"{other.LogicalName}\" from=\"{other.PrimaryIdAttribute}\" to=\"{otherAttribute}\" alias=\"{other.LogicalName}\" link-type=\"inner\">" +
                   (HasName(other) ? $"<attribute name=\"{other.PrimaryNameAttribute}\" />" : "") +
                   $"<filter><condition attribute=\"{other.PrimaryIdAttribute}\" operator=\"eq\" value=\"{SampleGuid}\" /></filter></link-entity></link-entity></entity></fetch>";
        }
        return null;
    }

    private static string Columns(EntityInfo entity)
    {
        var columns = $"<attribute name=\"{entity.PrimaryIdAttribute}\" />";
        if (HasName(entity))
        {
            columns += $"<attribute name=\"{entity.PrimaryNameAttribute}\" />";
        }
        return columns;
    }

    private static bool HasName(EntityInfo entity)
    {
        return !string.IsNullOrEmpty(entity.PrimaryNameAttribute) && entity.FindAttribute(entity.PrimaryNameAttribute) is not null;
    }

    private static string? LinkExample(EntityInfo root, IReadOnlyList<EntityInfo> entities)
    {
        var others = entities.Skip(1).ToDictionary(e => e.LogicalName, StringComparer.OrdinalIgnoreCase);
        foreach (var r in root.Relationships.OrderBy(r => r.SchemaName, StringComparer.Ordinal))
        {
            if (string.Equals(r.ReferencingEntity, root.LogicalName, StringComparison.OrdinalIgnoreCase)
                && others.TryGetValue(r.ReferencedEntity, out var referenced)
                && root.FindAttribute(r.ReferencingAttribute) is not null && referenced.FindAttribute(r.ReferencedAttribute) is not null)
            {
                return $"<fetch top=\"500\"><entity name=\"{root.LogicalName}\">{Columns(root)}" +
                       $"<link-entity name=\"{referenced.LogicalName}\" from=\"{r.ReferencedAttribute}\" to=\"{r.ReferencingAttribute}\" alias=\"{referenced.LogicalName}\" link-type=\"inner\">" +
                       (HasName(referenced) ? $"<attribute name=\"{referenced.PrimaryNameAttribute}\" />" : "") +
                       "</link-entity>" +
                       $"<filter><condition attribute=\"{r.ReferencingAttribute}\" operator=\"eq\" value=\"{SampleGuid}\" /></filter></entity></fetch>";
            }
            if (string.Equals(r.ReferencedEntity, root.LogicalName, StringComparison.OrdinalIgnoreCase)
                && others.TryGetValue(r.ReferencingEntity, out var referencing)
                && root.FindAttribute(r.ReferencedAttribute) is not null && referencing.FindAttribute(r.ReferencingAttribute) is not null)
            {
                return $"<fetch top=\"500\"><entity name=\"{root.LogicalName}\">{Columns(root)}" +
                       $"<link-entity name=\"{referencing.LogicalName}\" from=\"{r.ReferencingAttribute}\" to=\"{r.ReferencedAttribute}\" alias=\"{referencing.LogicalName}\" link-type=\"inner\">" +
                       (HasName(referencing) ? $"<attribute name=\"{referencing.PrimaryNameAttribute}\" />" : "") +
                       $"<filter><condition attribute=\"{referencing.PrimaryIdAttribute}\" operator=\"eq\" value=\"{SampleGuid}\" /></filter></link-entity></entity></fetch>";
            }
        }
        return null;
    }
}
