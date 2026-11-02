using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Reads a table's writable fields from CRM metadata using the service account, metadata only.
/// </summary>
public sealed class CrmCreateSchemaProvider : ICreateSchemaSource
{
    private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ownerid", "owningbusinessunit", "owninguser", "owningteam", "transactioncurrencyid", "exchangerate", "statecode", "statuscode", "createdon", "createdby",
        "modifiedon", "modifiedby", "createdonbehalfby", "modifiedonbehalfby", "versionnumber", "timezoneruleversionnumber", "utcconversiontimezonecode",
        "importsequencenumber", "overriddencreatedon", "processid", "stageid", "traversedpath", "entityimage", "slaid", "slainvokedid", "onholdtime", "followemail",
    };

    private readonly CrmClient _client;
    private readonly IMetadataSource _metadata;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new object();
    private readonly Dictionary<string, (DateTimeOffset At, CreateSchema? Schema)> _cache = new Dictionary<string, (DateTimeOffset, CreateSchema?)>(StringComparer.OrdinalIgnoreCase);

    public CrmCreateSchemaProvider(CrmClient client, IMetadataSource metadata, Func<DateTimeOffset>? clock = null, TimeSpan? ttl = null)
    {
        _client = client;
        _metadata = metadata;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Returns the writable-field schema of a table, or null when it is unknown.
    /// </summary>
    public async Task<CreateSchema?> GetAsync(string entity, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(entity, out var cached) && _clock() - cached.At < _ttl)
            {
                return cached.Schema;
            }
        }
        CreateSchema? schema;
        try
        {
            schema = await LoadAsync(entity, ct);
        }
        catch (CrmNlQuery.Application.Common.Exceptions.CrmException)
        {
            schema = null;
        }
        lock (_gate)
        {
            var at = schema is null ? _clock() - (_ttl - TimeSpan.FromMinutes(1)) : _clock();
            _cache[entity] = (at, schema);
        }
        return schema;
    }

    private async Task<CreateSchema?> LoadAsync(string entity, CancellationToken ct)
    {
        var baseUrl = $"EntityDefinitions(LogicalName='{entity}')";
        using var definition = await _client.GetAsServiceAsync(baseUrl +
            "?$select=LogicalName,EntitySetName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute,IsValidForAdvancedFind" +
            "&$expand=Attributes($select=LogicalName,AttributeType,DisplayName,RequiredLevel,IsValidForCreate,AttributeOf),ManyToOneRelationships($select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName)", ct);
        var root = definition.RootElement;
        var setName = Str(root, "EntitySetName");
        var primaryId = Str(root, "PrimaryIdAttribute");
        var primaryName = Str(root, "PrimaryNameAttribute");
        if (setName is null || primaryId is null || primaryName is null)
        {
            return null;
        }

        var lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in new[] { "StringAttributeMetadata", "MemoAttributeMetadata" })
        {
            using var typed = await _client.GetAsServiceAsync($"{baseUrl}/Attributes/Microsoft.Dynamics.CRM.{kind}?$select=LogicalName,MaxLength", ct);
            foreach (var a in typed.RootElement.GetProperty("value").EnumerateArray())
            {
                if (Str(a, "LogicalName") is { } name && a.TryGetProperty("MaxLength", out var max) && max.ValueKind == JsonValueKind.Number)
                {
                    lengths[name] = max.GetInt32();
                }
            }
        }

        var options = new Dictionary<string, IReadOnlyList<CreateOption>>(StringComparer.OrdinalIgnoreCase);
        using (var picklists = await _client.GetAsServiceAsync($"{baseUrl}/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options)", ct))
        {
            foreach (var a in picklists.RootElement.GetProperty("value").EnumerateArray())
            {
                if (Str(a, "LogicalName") is { } name && a.TryGetProperty("OptionSet", out var set))
                {
                    options[name] = MetadataParser.ParseOptions(set).Select(o => new CreateOption(o.Value, o.Label)).ToList();
                }
            }
        }

        var targets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        using (var lookups = await _client.GetAsServiceAsync($"{baseUrl}/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=LogicalName,Targets", ct))
        {
            foreach (var a in lookups.RootElement.GetProperty("value").EnumerateArray())
            {
                if (Str(a, "LogicalName") is { } name && a.TryGetProperty("Targets", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    targets[name] = list.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList();
                }
            }
        }

        var navigation = new Dictionary<(string Attribute, string Target), string>();
        if (root.TryGetProperty("ManyToOneRelationships", out var relations))
        {
            foreach (var r in relations.EnumerateArray())
            {
                if (Str(r, "ReferencingAttribute") is { } attribute && Str(r, "ReferencedEntity") is { } target && Str(r, "ReferencingEntityNavigationPropertyName") is { } nav)
                {
                    navigation[(attribute.ToLowerInvariant(), target.ToLowerInvariant())] = nav;
                }
            }
        }
        var entitySets = (await _metadata.ListEntitiesAsync(ct)).ToDictionary(e => e.LogicalName, e => e.EntitySetName, StringComparer.OrdinalIgnoreCase);

        var fields = new List<CreateField>();
        foreach (var a in root.GetProperty("Attributes").EnumerateArray())
        {
            var name = Str(a, "LogicalName");
            if (name is null || Ignored.Contains(name) || name == primaryId || name.EndsWith("_base", StringComparison.Ordinal) || name.Contains("yomi", StringComparison.Ordinal))
            {
                continue;
            }
            if (!a.TryGetProperty("IsValidForCreate", out var valid) || valid.ValueKind != JsonValueKind.True || a.TryGetProperty("AttributeOf", out var of) && of.ValueKind == JsonValueKind.String)
            {
                continue;
            }
            var label = Label(a, "DisplayName") ?? name;
            var required = a.TryGetProperty("RequiredLevel", out var level) && level.ValueKind == JsonValueKind.Object
                && Str(level, "Value") is "SystemRequired" or "ApplicationRequired";
            var type = Str(a, "AttributeType");
            var empty = Array.Empty<CreateOption>();
            var none = Array.Empty<LookupTarget>();
            CreateField? field = type switch
            {
                "String" => new CreateField(name, label, CreateFieldType.Text, required, lengths.TryGetValue(name, out var l) ? l : null, empty, none),
                "Memo" => new CreateField(name, label, CreateFieldType.LongText, required, lengths.TryGetValue(name, out var m) ? m : null, empty, none),
                "Integer" or "BigInt" => new CreateField(name, label, CreateFieldType.WholeNumber, required, null, empty, none),
                "Decimal" or "Double" or "Money" => new CreateField(name, label, CreateFieldType.Decimal, required, null, empty, none),
                "Boolean" => new CreateField(name, label, CreateFieldType.Boolean, required, null, empty, none),
                "DateTime" => new CreateField(name, label, CreateFieldType.Date, required, null, empty, none),
                "Picklist" when options.TryGetValue(name, out var o) && o.Count > 0 => new CreateField(name, label, CreateFieldType.Choice, required, null, o, none),
                "Lookup" or "Customer" when TargetsOf(name, type, targets, navigation) is { Count: > 0 } t => LookupField(name, label, required, t, navigation, entitySets),
                _ => null,
            };
            if (field is not null)
            {
                fields.Add(field);
            }
            else if (required)
            {
                return null;
            }
        }

        var primary = fields.FirstOrDefault(f => f.Name == primaryName);
        if (primary is null)
        {
            var last = fields.FirstOrDefault(f => f.Name == "lastname" && f.Type == CreateFieldType.Text);
            if (last is null)
            {
                return null;
            }
            if (!last.Required)
            {
                fields[fields.IndexOf(last)] = last with { Required = true };
            }
        }
        else if (!primary.Required)
        {
            fields[fields.IndexOf(primary)] = primary with { Required = true };
        }
        return new CreateSchema(entity, setName, Label(root, "DisplayName") ?? entity, primaryId, primaryName, fields);
    }

    private static IReadOnlyList<string>? TargetsOf(string name, string? type, Dictionary<string, IReadOnlyList<string>> targets, Dictionary<(string Attribute, string Target), string> navigation)
    {
        if (targets.TryGetValue(name, out var typed))
        {
            return typed;
        }
        if (type != "Customer")
        {
            return null;
        }
        return navigation.Keys.Where(k => k.Attribute == name.ToLowerInvariant()).Select(k => k.Target).ToList();
    }

    private static CreateField? LookupField(string name, string label, bool required, IReadOnlyList<string> targets,
        Dictionary<(string Attribute, string Target), string> navigation, Dictionary<string, string> entitySets)
    {
        var list = new List<LookupTarget>();
        foreach (var target in targets)
        {
            if (navigation.TryGetValue((name.ToLowerInvariant(), target.ToLowerInvariant()), out var nav) && entitySets.TryGetValue(target, out var set))
            {
                list.Add(new LookupTarget(target, set, nav));
            }
        }
        return list.Count == 0 ? (required ? null : null) : new CreateField(name, label, CreateFieldType.Lookup, required, null, Array.Empty<CreateOption>(), list);
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Label(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("UserLocalizedLabel", out var loc) || loc.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return Str(loc, "Label");
    }
}
