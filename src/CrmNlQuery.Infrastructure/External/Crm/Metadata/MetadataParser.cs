using System.Text.Json;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Parses CRM Web API metadata JSON into entity, attribute, relationship and option models.
/// </summary>
public static class MetadataParser
{
    /// <summary>
    /// Parses the entity list JSON into entity summaries.
    /// </summary>
    public static IReadOnlyList<EntitySummary> ParseEntityList(JsonElement root)
    {
        var list = new List<EntitySummary>();
        foreach (var e in root.GetProperty("value").EnumerateArray())
        {
            var isPrivate = e.TryGetProperty("IsPrivate", out var p) && p.ValueKind == JsonValueKind.True;
            var setName = StringOrNull(e, "EntitySetName");
            if (isPrivate || string.IsNullOrEmpty(setName))
            {
                continue;
            }
            var logical = e.GetProperty("LogicalName").GetString()!;
            list.Add(new EntitySummary(logical, setName, Label(e, "DisplayName") ?? logical));
        }
        return list.OrderBy(x => x.LogicalName, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Parses one entity's JSON into entity info, attaching option labels by attribute.
    /// </summary>
    public static EntityInfo ParseEntity(JsonElement definition, IReadOnlyDictionary<string, IReadOnlyList<OptionInfo>> optionsByAttribute)
    {
        var logical = definition.GetProperty("LogicalName").GetString()!;

        var attributes = new List<AttributeInfo>();
        foreach (var a in definition.GetProperty("Attributes").EnumerateArray())
        {
            var name = a.GetProperty("LogicalName").GetString()!;
            var options = optionsByAttribute.TryGetValue(name, out var found) ? found : new List<OptionInfo>();
            var attributeType = StringOrNull(a, "AttributeType") ?? "";
            if (a.TryGetProperty("AttributeTypeName", out var typeName) && typeName.ValueKind == JsonValueKind.Object
                && StringOrNull(typeName, "Value") == "MultiSelectPicklistType")
            {
                attributeType = "MultiSelectPicklist";
            }
            attributes.Add(new AttributeInfo(name, Label(a, "DisplayName") ?? name, attributeType, options));
        }

        var relationships = new Dictionary<string, RelationshipInfo>(StringComparer.Ordinal);
        foreach (var listName in new[] { "ManyToOneRelationships", "OneToManyRelationships" })
        {
            if (!definition.TryGetProperty(listName, out var rels))
            {
                continue;
            }
            foreach (var r in rels.EnumerateArray())
            {
                var info = new RelationshipInfo(
                    r.GetProperty("SchemaName").GetString()!,
                    r.GetProperty("ReferencingEntity").GetString()!,
                    r.GetProperty("ReferencingAttribute").GetString()!,
                    r.GetProperty("ReferencedEntity").GetString()!,
                    r.GetProperty("ReferencedAttribute").GetString()!);
                relationships[info.SchemaName] = info;
            }
        }

        var manyToMany = new Dictionary<string, ManyToManyInfo>(StringComparer.Ordinal);
        if (definition.TryGetProperty("ManyToManyRelationships", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in links.EnumerateArray())
            {
                var schema = StringOrNull(m, "SchemaName");
                var intersect = StringOrNull(m, "IntersectEntityName");
                var entity1 = StringOrNull(m, "Entity1LogicalName");
                var attribute1 = StringOrNull(m, "Entity1IntersectAttribute");
                var entity2 = StringOrNull(m, "Entity2LogicalName");
                var attribute2 = StringOrNull(m, "Entity2IntersectAttribute");
                if (schema is null || intersect is null || entity1 is null || attribute1 is null || entity2 is null || attribute2 is null)
                {
                    continue;
                }
                manyToMany[schema] = new ManyToManyInfo(schema, intersect, entity1, attribute1, entity2, attribute2);
            }
        }

        return new EntityInfo(
            logical,
            StringOrNull(definition, "EntitySetName") ?? logical,
            Label(definition, "DisplayName") ?? logical,
            StringOrNull(definition, "PrimaryIdAttribute") ?? "",
            StringOrNull(definition, "PrimaryNameAttribute") ?? "",
            attributes,
            relationships.Values.ToList())
        {
            ManyToMany = manyToMany.Values.OrderBy(x => x.SchemaName, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// Parses option set JSON into a list of option values and labels.
    /// </summary>
    public static IReadOnlyList<OptionInfo> ParseOptions(JsonElement optionSet)
    {
        var list = new List<OptionInfo>();
        if (optionSet.ValueKind == JsonValueKind.Object && optionSet.TryGetProperty("TrueOption", out var trueOption) && optionSet.TryGetProperty("FalseOption", out var falseOption)
            && trueOption.ValueKind == JsonValueKind.Object && falseOption.ValueKind == JsonValueKind.Object)
        {
            list.Add(new OptionInfo(trueOption.GetProperty("Value").GetInt32(), Label(trueOption, "Label") ?? "True"));
            list.Add(new OptionInfo(falseOption.GetProperty("Value").GetInt32(), Label(falseOption, "Label") ?? "False"));
            return list;
        }
        if (optionSet.ValueKind != JsonValueKind.Object || !optionSet.TryGetProperty("Options", out var options))
        {
            return list;
        }
        foreach (var o in options.EnumerateArray())
        {
            list.Add(new OptionInfo(o.GetProperty("Value").GetInt32(), Label(o, "Label") ?? ""));
        }
        return list;
    }

    private static string? StringOrNull(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? Label(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!p.TryGetProperty("UserLocalizedLabel", out var loc) || loc.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return StringOrNull(loc, "Label");
    }
}
