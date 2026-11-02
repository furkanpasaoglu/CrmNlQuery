using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Planning.Prompts;

/// <summary>
/// Shrinks the entity blocks given to the planner by removing only clearly useless attributes.
/// </summary>
public static class PlanContextPruner
{
    private static readonly HashSet<string> AlwaysKept = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "statecode", "statuscode", "ownerid", "createdon", "modifiedon", "createdby", "modifiedby",
    };

    private static readonly HashSet<string> TrackingFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "importsequencenumber", "timezoneruleversionnumber", "utcconversiontimezonecode", "overriddencreatedon", "processid", "stageid", "traversedpath",
        "exchangerate", "createdonbehalfby", "modifiedonbehalfby", "createdbyexternalparty", "modifiedbyexternalparty",
    };

    private static readonly HashSet<string> PointlessTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EntityName", "BigInt", "ManagedProperty" };

    private static readonly Regex SecondAddress = new Regex(@"address\s*2|second address|2nd address|ikinci adres|2\. adres|adres\s*2|adresi 2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ThirdAddress = new Regex(@"address\s*3|third address|3rd address|üçüncü adres|ucuncu adres|3\. adres|adres\s*3", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WordSplit = new Regex(@"[^a-z0-9_]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Prunes the planner request's entity blocks to cut prompt size.
    /// </summary>
    public static IReadOnlyList<EntityInfo> Prune(PlanRequest request)
    {
        var text = string.Join("\n", new[] { request.Question, string.Join(" ", request.Columns), request.Previous?.FetchXml ?? "", request.Previous?.Question ?? "", string.Join("\n", request.Knowledge ?? Array.Empty<string>()) });
        var lower = text.ToLowerInvariant();
        var mentioned = new HashSet<string>(WordSplit.Split(lower).Where(w => w.Length > 0), StringComparer.Ordinal);
        var wantsSecond = SecondAddress.IsMatch(text);
        var wantsThird = ThirdAddress.IsMatch(text);
        var columns = new HashSet<string>(request.Columns, StringComparer.OrdinalIgnoreCase);
        var entityNames = new HashSet<string>(request.Entities.Select(e => e.LogicalName), StringComparer.OrdinalIgnoreCase);

        var result = new List<EntityInfo>(request.Entities.Count);
        foreach (var entity in request.Entities)
        {
            var all = new HashSet<string>(entity.Attributes.Select(a => a.LogicalName), StringComparer.OrdinalIgnoreCase);
            var attributes = entity.Attributes.Where(a => Keep(a, entity, all, columns, mentioned, wantsSecond, wantsThird)).ToList();
            var relationships = entity.Relationships
                .Where(r => entityNames.Contains(r.ReferencingEntity) && entityNames.Contains(r.ReferencedEntity))
                .ToList();
            result.Add(entity with { Attributes = attributes, Relationships = relationships });
        }
        return result;
    }

    private static bool Keep(AttributeInfo a, EntityInfo entity, HashSet<string> all, HashSet<string> columns, HashSet<string> mentioned, bool wantsSecond, bool wantsThird)
    {
        var name = a.LogicalName;
        if (string.Equals(name, entity.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase) || string.Equals(name, entity.PrimaryNameAttribute, StringComparison.OrdinalIgnoreCase)
            || columns.Contains(name) || AlwaysKept.Contains(name) || mentioned.Contains(name.ToLowerInvariant()))
        {
            return true;
        }
        if (a.AttributeType is "Lookup" or "Owner" or "Customer" && !TrackingFields.Contains(name))
        {
            return true;
        }
        if (PointlessTypes.Contains(a.AttributeType) || TrackingFields.Contains(name))
        {
            return false;
        }
        if (name.Contains("yomi", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (a.AttributeType == "Virtual" && name.EndsWith("name", StringComparison.OrdinalIgnoreCase) && name.Length > 4 && all.Contains(name.Substring(0, name.Length - 4)))
        {
            return false;
        }
        if (name.EndsWith("_base", StringComparison.OrdinalIgnoreCase) && a.AttributeType == "Money" && all.Contains(name.Substring(0, name.Length - 5)))
        {
            return false;
        }
        if (name.StartsWith("address2_", StringComparison.OrdinalIgnoreCase))
        {
            return wantsSecond;
        }
        if (name.StartsWith("address3_", StringComparison.OrdinalIgnoreCase))
        {
            return wantsThird;
        }
        return true;
    }
}
