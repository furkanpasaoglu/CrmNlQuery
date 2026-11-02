using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Resolves the record open on the user's CRM form, treating the client-sent entity and id as untrusted and reading the record as the caller.
/// </summary>
public sealed class RecordContextResolver
{
    public const string Mention = "this record";
    private const int MaxNameChars = 120;

    private readonly IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;

    public RecordContextResolver(IMetadataSource metadata, IQueryExecutor executor)
    {
        _metadata = metadata;
        _executor = executor;
    }

    /// <summary>
    /// Validates the entity and id, reads the record with the caller's permissions, and returns it or null.
    /// </summary>
    public async Task<ResolvedRecord?> ResolveAsync(string? entity, string? id, Caller caller, CancellationToken ct = default)
    {
        using var accessScope = CrmNlQuery.Application.Observability.AccessLog.AccessLogContext.Begin("record_context");
        if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id.Trim(), out var guid) || guid == Guid.Empty)
        {
            return null;
        }
        var info = await _metadata.GetEntityAsync(entity.Trim().ToLowerInvariant(), ct);
        if (info is null || string.IsNullOrEmpty(info.PrimaryIdAttribute) || string.IsNullOrEmpty(info.PrimaryNameAttribute))
        {
            return null;
        }

        var fetch = new XElement("fetch", new XAttribute("top", "1"),
            new XElement("entity", new XAttribute("name", info.LogicalName),
                new XElement("attribute", new XAttribute("name", info.PrimaryNameAttribute)),
                new XElement("filter",
                    new XElement("condition", new XAttribute("attribute", info.PrimaryIdAttribute), new XAttribute("operator", "eq"), new XAttribute("value", guid.ToString())))));
        try
        {
            var table = await _executor.ExecuteAsync(info.EntitySetName, fetch.ToString(SaveOptions.DisableFormatting), caller, ct);
            if (table.Rows.Count == 0)
            {
                return null;
            }
            var column = table.Columns.ToList().FindIndex(c => string.Equals(c, info.PrimaryNameAttribute, StringComparison.OrdinalIgnoreCase));
            var name = PromptText.Clean(table.Rows[0].Count > Math.Max(0, column) ? table.Rows[0][Math.Max(0, column)] : "", MaxNameChars);
            return new ResolvedRecord(Mention, info.LogicalName, guid, name.Length == 0 ? info.Label : name);
        }
        catch (CrmException)
        {
            return null;
        }
    }
}
