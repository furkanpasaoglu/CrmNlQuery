using System.Text.Json;
using System.Text.RegularExpressions;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Provides CRM schema metadata with caching, serving stale data if the CRM errors.
/// </summary>
public sealed class MetadataProvider : IMetadataSource
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private static readonly Regex SafeName = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly string[] OptionAttributeTypes = { "Picklist", "State", "Status", "Boolean", "MultiSelectPicklist" };

    private const int CacheVersion = 3;

    private readonly CrmClient _client;
    private readonly string _cacheDir;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan> _ttl;

    public MetadataProvider(CrmClient client, string cacheDir, Func<DateTimeOffset> now, Func<TimeSpan>? ttl = null)
    {
        _ttl = ttl ?? (() => DefaultTtl);
        _client = client;
        _cacheDir = cacheDir;
        _now = now;
    }

    /// <summary>
    /// Lists entity summaries from the cache or the CRM.
    /// </summary>
    public async Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default)
    {
        var cached = ReadCache<List<EntitySummary>>("entities.json");
        if (cached is not null)
        {
            return cached;
        }
        try
        {
            using var doc = await _client.GetAsServiceAsync("EntityDefinitions?$select=LogicalName,EntitySetName,DisplayName,IsPrivate", ct);
            var list = MetadataParser.ParseEntityList(doc.RootElement).ToList();
            WriteCache("entities.json", list);
            return list;
        }
        catch (CrmException)
        {
            var stale = ReadCache<List<EntitySummary>>("entities.json", allowStale: true);
            if (stale is not null)
            {
                return stale;
            }
            throw;
        }
    }

    /// <summary>
    /// Gets one entity's metadata, or null if unknown.
    /// </summary>
    public async Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(logicalName) || !SafeName.IsMatch(logicalName))
        {
            return null;
        }

        var file = "entity-" + logicalName + ".json";
        var cached = ReadCache<EntityInfo>(file);
        if (cached is not null)
        {
            return cached;
        }

        JsonDocument definition;
        try
        {
            definition = await _client.GetAsServiceAsync(DefinitionUrl(logicalName), ct);
        }
        catch (CrmException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (CrmException)
        {
            var stale = ReadCache<EntityInfo>(file, allowStale: true);
            if (stale is not null)
            {
                return stale;
            }
            throw;
        }

        using (definition)
        {
            var options = new Dictionary<string, IReadOnlyList<OptionInfo>>(StringComparer.Ordinal);
            foreach (var type in OptionAttributeTypes)
            {
                var optionFields = type == "Boolean" ? "TrueOption,FalseOption" : "Options";
                var url = $"EntityDefinitions(LogicalName='{logicalName}')/Attributes/Microsoft.Dynamics.CRM.{type}AttributeMetadata" +
                          $"?$select=LogicalName&$expand=OptionSet($select={optionFields})";
                JsonDocument optionDoc;
                try
                {
                    optionDoc = await _client.GetAsServiceAsync(url, ct);
                }
                catch (CrmException) when (type == "MultiSelectPicklist")
                {
                    continue;
                }
                using (optionDoc)
                {
                    foreach (var a in optionDoc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        if (a.TryGetProperty("OptionSet", out var set))
                        {
                            options[a.GetProperty("LogicalName").GetString()!] = MetadataParser.ParseOptions(set);
                        }
                    }
                }
            }

            var entity = MetadataParser.ParseEntity(definition.RootElement, options);
            WriteCache(file, entity);
            return entity;
        }
    }

    /// <summary>
    /// Refreshes the cached metadata from the CRM.
    /// </summary>
    public Task RefreshAsync(CancellationToken ct = default)
    {
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir, "*.json"))
            {
                File.Delete(file);
            }
        }
        return Task.CompletedTask;
    }

    private static string DefinitionUrl(string logicalName)
    {
        const string relFields = "SchemaName,ReferencingEntity,ReferencingAttribute,ReferencedEntity,ReferencedAttribute";
        return $"EntityDefinitions(LogicalName='{logicalName}')" +
               "?$select=LogicalName,EntitySetName,DisplayName,PrimaryIdAttribute,PrimaryNameAttribute" +
               "&$expand=Attributes($select=LogicalName,AttributeType,AttributeTypeName,DisplayName)," +
               $"ManyToOneRelationships($select={relFields}),OneToManyRelationships($select={relFields})," +
               "ManyToManyRelationships($select=SchemaName,IntersectEntityName,Entity1LogicalName,Entity1IntersectAttribute,Entity2LogicalName,Entity2IntersectAttribute)";
    }

    private T? ReadCache<T>(string file, bool allowStale = false) where T : class
    {
        var path = Path.Combine(_cacheDir, file);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != CacheVersion)
            {
                return null;
            }
            var fetchedAt = doc.RootElement.GetProperty("fetchedAtUtc").GetDateTimeOffset();
            if (!allowStale && _now() - fetchedAt > _ttl())
            {
                return null;
            }
            return doc.RootElement.GetProperty("payload").Deserialize<T>();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IOException
                                   or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private void WriteCache<T>(string file, T payload)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var wrapper = new { version = CacheVersion, fetchedAtUtc = _now(), payload };
            File.WriteAllText(Path.Combine(_cacheDir, file), JsonSerializer.Serialize(wrapper));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
