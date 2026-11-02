using System.Globalization;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Summary.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Summary.Services;

/// <summary>
/// Builds summary data from its inputs.
/// </summary>
public sealed class SummaryBuilder
{
    private const int MaxParallel = 4;
    private const int ExpiryDays = 90;
    private const int MaxViaSections = 8;

    private readonly IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;
    private readonly IDefaultViewSource _views;
    private readonly Func<TimeSpan> _sectionTimeout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger? _logger;

    public SummaryBuilder(IMetadataSource metadata, IQueryExecutor executor, IDefaultViewSource views, Func<TimeSpan>? sectionTimeout = null,
        Func<DateTimeOffset>? clock = null, ILogger? logger = null)
    {
        _metadata = metadata;
        _executor = executor;
        _views = views;
        _sectionTimeout = sectionTimeout ?? (() => TimeSpan.FromSeconds(15));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    private sealed record Target(string Kind, EntityInfo Table, IReadOnlyList<string> Lookups, EntityInfo? Via = null, string? LinkLookup = null);

    /// <summary>
    /// Builds a customer summary for an account or contact by running fixed per-section queries as the caller; knownOnly limits it to the five known types for lightweight draft reads.
    /// </summary>
    public async Task<SummaryOutcome> BuildAsync(Caller caller, string entity, Guid id, string name, CancellationToken ct = default, bool knownOnly = false)
    {
        var root = await _metadata.GetEntityAsync(entity, ct);
        if (root is null)
        {
            return new SummaryOutcome(entity, entity, id.ToString("N"), name, new List<SummarySection> { SummarySection.Unavailable(SummaryKinds.Basic, entity, entity) });
        }

        var gate = new SemaphoreSlim(MaxParallel);
        var jobs = new List<Task<SummarySection?>>();
        if (!knownOnly)
        {
            jobs.Add(RunAsync(gate, SummaryKinds.Basic, root.LogicalName, root.Label, null, token => BasicAsync(caller, root, id, token), ct));
        }
        foreach (var target in await DiscoverAsync(root, ct))
        {
            if (knownOnly && target.Kind == SummaryKinds.Generic)
            {
                continue;
            }
            var t = target;
            jobs.Add(RunAsync(gate, t.Kind, t.Table.LogicalName, t.Table.Label, t.Via, token => SectionAsync(caller, t, id, token), ct));
        }

        var sections = (await Task.WhenAll(jobs)).Where(s => s is not null && !(s.Kind == SummaryKinds.Generic && s.Status == SummaryStatus.Empty)).Select(s => s!).ToList();
        return new SummaryOutcome(root.LogicalName, root.Label, id.ToString("N"), name, sections);
    }

    /// <summary>
    /// Builds for record for summary builder.
    /// </summary>
    public async Task<SummaryOutcome> BuildForRecordAsync(Caller caller, string entity, Guid id, CancellationToken ct = default)
    {
        var root = await _metadata.GetEntityAsync(entity, ct);
        var name = "";
        if (root is not null)
        {
            try
            {
                var lookup = new XElement("entity", new XAttribute("name", root.LogicalName), new XElement("attribute", new XAttribute("name", root.PrimaryNameAttribute)),
                    new XElement("filter", Condition(root.PrimaryIdAttribute, "eq", id.ToString())));
                var table = await RunQueryAsync(root, Fetch(lookup, top: 1), caller, ct);
                name = table.Rows.Count > 0 ? Cell(table, 0, root.PrimaryNameAttribute) : "";
            }
            catch (CrmException)
            {
                name = "";
            }
        }
        return await BuildAsync(caller, entity, id, name, ct);
    }

    /// <summary>
    /// Builds the list query for one summary section without a top limit, ordered, from the schema only; returns null if the section does not apply.
    /// </summary>
    public async Task<string?> ListFetchXmlAsync(string rootEntity, Guid rootId, string kind, string entity, string? viaEntity, CancellationToken ct = default)
    {
        var root = await _metadata.GetEntityAsync(rootEntity, ct);
        if (root is null || kind == SummaryKinds.Basic)
        {
            return null;
        }
        var target = (await DiscoverAsync(root, ct)).FirstOrDefault(t => t.Kind == kind
            && string.Equals(t.Table.LogicalName, entity, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Via?.LogicalName, string.IsNullOrEmpty(viaEntity) ? null : viaEntity, StringComparison.OrdinalIgnoreCase));
        return target is null ? null : Fetch(ListEntity(target, rootId)).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Builds list queries for all summary sections in one schema discovery pass, keyed by kind, table and intermediate table, so Open in CRM availability needs no repeated reads.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ListFetchesAsync(string rootEntity, Guid rootId, CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = await _metadata.GetEntityAsync(rootEntity, ct);
        if (root is null)
        {
            return map;
        }
        foreach (var target in await DiscoverAsync(root, ct))
        {
            map[SectionKey(target.Kind, target.Table.LogicalName, target.Via?.LogicalName)] = Fetch(ListEntity(target, rootId)).ToString(SaveOptions.DisableFormatting);
        }
        return map;
    }

    /// <summary>
    /// Handles section key for summary builder.
    /// </summary>
    public static string SectionKey(string kind, string entity, string? viaEntity) => kind + "|" + entity + "|" + (viaEntity ?? "");

    private sealed record KnownSpec(string Kind, string Entity, string Lookup, string[] Needs);

    private static readonly KnownSpec[] Known =
    {
        new KnownSpec(SummaryKinds.Opportunities, "opportunity", "customerid", new[] { "statecode" }),
        new KnownSpec(SummaryKinds.Activities, "activitypointer", "regardingobjectid", new[] { "subject", "statecode" }),
        new KnownSpec(SummaryKinds.Cases, "incident", "customerid", new[] { "statecode" }),
        new KnownSpec(SummaryKinds.Contracts, "contract", "customerid", new[] { "statecode" }),
        new KnownSpec(SummaryKinds.Orders, "salesorder", "customerid", new[] { "statecode" }),
    };

    private async Task<IReadOnlyList<Target>> DiscoverAsync(EntityInfo root, CancellationToken ct)
    {
        var tables = new List<EntityInfo>();
        foreach (var summary in await _metadata.ListEntitiesAsync(ct))
        {
            if (string.Equals(summary.LogicalName, root.LogicalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var info = await _metadata.GetEntityAsync(summary.LogicalName, ct);
            if (info is not null)
            {
                tables.Add(info);
            }
        }

        var targets = new List<Target>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Known)
        {
            var table = tables.FirstOrDefault(t => string.Equals(t.LogicalName, spec.Entity, StringComparison.OrdinalIgnoreCase));
            known.Add(spec.Entity);
            if (table is null || !LooksUpTo(table, root, spec.Lookup) || spec.Needs.Any(a => table.FindAttribute(a) is null))
            {
                continue;
            }
            targets.Add(new Target(spec.Kind, table, new[] { spec.Lookup }));
        }

        var direct = new List<Target>();
        foreach (var table in tables.Where(t => !known.Contains(t.LogicalName)).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase))
        {
            var lookups = LookupsTo(table, root);
            if (lookups.Count > 0)
            {
                direct.Add(new Target(SummaryKinds.Generic, table, lookups));
            }
        }
        targets.AddRange(direct);

        var directNames = new HashSet<string>(direct.Select(d => d.Table.LogicalName), StringComparer.OrdinalIgnoreCase);
        var via = new List<Target>();
        foreach (var table in tables.Where(t => !known.Contains(t.LogicalName) && !directNames.Contains(t.LogicalName)).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var parent in direct.OrderBy(d => d.Table.Label, StringComparer.OrdinalIgnoreCase))
            {
                var lookup = LookupsTo(table, parent.Table).FirstOrDefault();
                if (lookup is not null)
                {
                    via.Add(new Target(SummaryKinds.Generic, table, parent.Lookups, parent.Table, lookup));
                }
            }
        }
        targets.AddRange(via.Take(MaxViaSections));
        return targets;
    }

    private static bool LooksUpTo(EntityInfo table, EntityInfo root, string attribute)
        => LookupsTo(table, root).Contains(attribute, StringComparer.OrdinalIgnoreCase) && table.FindAttribute(attribute) is not null;

    private static IReadOnlyList<string> LookupsTo(EntityInfo table, EntityInfo target)
    {
        return table.Relationships
            .Where(r => string.Equals(r.ReferencingEntity, table.LogicalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ReferencedEntity, target.LogicalName, StringComparison.OrdinalIgnoreCase)
                && table.FindAttribute(r.ReferencingAttribute) is not null)
            .Select(r => r.ReferencingAttribute)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<XElement> StateConditions(Target t) => t.Kind switch
    {
        SummaryKinds.Opportunities or SummaryKinds.Cases => new List<XElement> { Condition("statecode", "eq", "0") },
        SummaryKinds.Contracts => new List<XElement> { Condition("statecode", "eq", "2") },
        SummaryKinds.Orders => new List<XElement> { new XElement("condition", new XAttribute("attribute", "statecode"), new XAttribute("operator", "in"), new XElement("value", "0"), new XElement("value", "1")) },
        _ => new List<XElement>(),
    };

    private static IEnumerable<XElement> RootConditions(Target t, Guid id)
    {
        if (t.Via is not null)
        {
            return Array.Empty<XElement>();
        }
        return t.Lookups.Count == 1
            ? new[] { Condition(t.Lookups[0], "eq", id.ToString()) }
            : new[] { new XElement("filter", new XAttribute("type", "or"), t.Lookups.Select(l => Condition(l, "eq", id.ToString()))) };
    }

    private static XElement? LinkElement(Target t, Guid id)
    {
        if (t.Via is null)
        {
            return null;
        }
        var link = new XElement("link-entity", new XAttribute("name", t.Via.LogicalName), new XAttribute("from", t.Via.PrimaryIdAttribute), new XAttribute("to", t.LinkLookup!),
            new XAttribute("alias", "via"), new XAttribute("link-type", "inner"));
        link.Add(t.Lookups.Count == 1
            ? new XElement("filter", Condition(t.Lookups[0], "eq", id.ToString()))
            : new XElement("filter", new XAttribute("type", "or"), t.Lookups.Select(l => Condition(l, "eq", id.ToString()))));
        return link;
    }

    private static IEnumerable<XElement> AllConditions(Target t, Guid id) => RootConditions(t, id).Concat(StateConditions(t));

    private static XElement ListEntity(Target t, Guid id)
    {
        var table = t.Table;
        var wantsValue = t.Kind == SummaryKinds.Opportunities && table.FindAttribute("estimatedvalue") is not null;
        var columns = t.Kind switch
        {
            SummaryKinds.Opportunities => new List<string?> { table.PrimaryNameAttribute, "estimatedvalue", "estimatedclosedate" },
            SummaryKinds.Activities => new List<string?> { "subject", "activitytypecode", "actualend", "scheduledend", "statecode" },
            SummaryKinds.Cases => new List<string?> { table.PrimaryNameAttribute, "createdon", "prioritycode" },
            SummaryKinds.Contracts => new List<string?> { table.PrimaryNameAttribute, "expireson" },
            _ => new List<string?> { table.PrimaryNameAttribute, "createdon" },
        };
        var entity = new XElement("entity", new XAttribute("name", table.LogicalName));
        foreach (var column in columns.Where(c => c is not null && table.FindAttribute(c) is not null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            entity.Add(new XElement("attribute", new XAttribute("name", column!)));
        }
        entity.Add(new XElement("attribute", new XAttribute("name", table.PrimaryIdAttribute), new XAttribute("alias", DistinctRewriter.RowIdAlias)));
        var conditions = AllConditions(t, id).ToList();
        if (conditions.Count > 0)
        {
            entity.Add(new XElement("filter", conditions));
        }
        if (LinkElement(t, id) is { } link) { entity.Add(link); }
        var (orderBy, descending) = t.Kind switch
        {
            SummaryKinds.Opportunities => (wantsValue ? "estimatedvalue" : "createdon", true),
            SummaryKinds.Activities => ("modifiedon", true),
            SummaryKinds.Cases => ("createdon", false),
            SummaryKinds.Contracts => ("expireson", false),
            _ => ("createdon", true),
        };
        if (table.FindAttribute(orderBy) is null && t.Kind == SummaryKinds.Activities) { orderBy = "createdon"; }
        if (table.FindAttribute(orderBy) is not null)
        {
            entity.Add(new XElement("order", new XAttribute("attribute", orderBy), new XAttribute("descending", descending ? "true" : "false")));
        }
        return entity;
    }

    private Task<SummarySection> SectionAsync(Caller caller, Target t, Guid id, CancellationToken ct) => t.Kind switch
    {
        SummaryKinds.Opportunities => OpportunitiesAsync(caller, t, id, ct),
        SummaryKinds.Activities => ActivitiesAsync(caller, t, id, ct),
        SummaryKinds.Cases => CasesAsync(caller, t, id, ct),
        SummaryKinds.Contracts => ContractsAsync(caller, t, id, ct),
        SummaryKinds.Orders => OrdersAsync(caller, t, id, ct),
        _ => GenericAsync(caller, t, id, ct),
    };

    private async Task<SummarySection> BasicAsync(Caller caller, EntityInfo root, Guid id, CancellationToken ct)
    {
        var columns = new List<string> { root.PrimaryNameAttribute };
        if (root.FindAttribute("ownerid") is not null) { columns.Add("ownerid"); }
        if (root.FindAttribute("createdon") is not null) { columns.Add("createdon"); }
        var viewColumns = await _views.GetDefaultColumnsAsync(root.LogicalName, ct);
        foreach (var column in viewColumns)
        {
            var attribute = root.FindAttribute(column);
            if (columns.Count >= 8 || attribute is null || attribute.AttributeType == "Uniqueidentifier" || columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            columns.Add(column);
        }
        var entity = new XElement("entity", new XAttribute("name", root.LogicalName));
        foreach (var column in columns) { entity.Add(new XElement("attribute", new XAttribute("name", column))); }
        entity.Add(new XElement("filter", Condition(root.PrimaryIdAttribute, "eq", id.ToString())));
        var table = await RunQueryAsync(root, Fetch(entity, top: 1), caller, ct);
        if (table.Rows.Count == 0)
        {
            return Section(SummaryKinds.Basic, root, null, Array.Empty<SummaryMetric>(), Array.Empty<SummaryItem>());
        }
        var metrics = new List<SummaryMetric>();
        foreach (var column in columns.Skip(1))
        {
            var value = Cell(table, 0, column);
            if (!string.IsNullOrWhiteSpace(value))
            {
                metrics.Add(new SummaryMetric(column, root.FindAttribute(column)?.Label ?? column, value));
            }
        }
        return Section(SummaryKinds.Basic, root, null, metrics, Array.Empty<SummaryItem>(), forceOk: true);
    }

    private async Task<SummarySection> OpportunitiesAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var table = t.Table;
        var hasValue = table.FindAttribute("estimatedvalue") is not null;
        var count = await CountAsync(caller, t, id, hasValue ? "estimatedvalue" : null, ct);
        var metrics = new List<SummaryMetric> { new SummaryMetric("count", "Open opportunities", count.Count.ToString(CultureInfo.InvariantCulture)) };
        if (hasValue && count.Sum is not null) { metrics.Add(new SummaryMetric("total", "Total value", count.Sum!)); }
        var items = new List<SummaryItem>();
        if (count.Count > 0)
        {
            var rows = await RunQueryAsync(table, Fetch(ListEntity(t, id), top: 3), caller, ct);
            for (var i = 0; i < rows.Rows.Count; i++)
            {
                items.Add(new SummaryItem(Cell(rows, i, table.PrimaryNameAttribute), Join(Cell(rows, i, "estimatedvalue"), Cell(rows, i, "estimatedclosedate")), table.LogicalName, RowId(rows, i)));
            }
        }
        return Section(SummaryKinds.Opportunities, table, null, metrics, items, forceOk: count.Count > 0);
    }

    private async Task<SummarySection> ActivitiesAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var table = t.Table;
        var rows = await RunQueryAsync(table, Fetch(ListEntity(t, id), top: 5), caller, ct);
        var items = new List<SummaryItem>();
        for (var i = 0; i < rows.Rows.Count; i++)
        {
            var when = Cell(rows, i, "actualend");
            if (string.IsNullOrEmpty(when)) { when = Cell(rows, i, "scheduledend"); }
            items.Add(new SummaryItem(Cell(rows, i, "subject"), Join(Cell(rows, i, "activitytypecode"), when, Cell(rows, i, "statecode"))));
        }
        var metrics = new List<SummaryMetric>();
        if (table.FindAttribute("actualend") is not null)
        {
            var last = new XElement("entity", new XAttribute("name", table.LogicalName), new XElement("attribute", new XAttribute("name", "actualend")),
                new XElement("filter", RootConditions(t, id).Concat(new[] { Condition("statecode", "eq", "1"), Condition("actualend", "not-null", null) })),
                new XElement("order", new XAttribute("attribute", "actualend"), new XAttribute("descending", "true")));
            var lastRows = await RunQueryAsync(table, Fetch(last, top: 1), caller, ct);
            metrics.Add(new SummaryMetric("last_contact", "Last contact", lastRows.Rows.Count > 0 ? Cell(lastRows, 0, "actualend") : "none"));
        }
        return Section(SummaryKinds.Activities, table, null, metrics, items, forceOk: items.Count > 0);
    }

    private async Task<SummarySection> CasesAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var table = t.Table;
        var count = await CountAsync(caller, t, id, null, ct);
        var metrics = new List<SummaryMetric> { new SummaryMetric("count", "Open cases", count.Count.ToString(CultureInfo.InvariantCulture)) };
        var items = new List<SummaryItem>();
        if (count.Count > 0)
        {
            var rows = await RunQueryAsync(table, Fetch(ListEntity(t, id), top: 1), caller, ct);
            if (rows.Rows.Count > 0 && table.FindAttribute("createdon") is not null)
            {
                metrics.Add(new SummaryMetric("oldest", "Oldest open case", Join(Cell(rows, 0, table.PrimaryNameAttribute), Cell(rows, 0, "createdon")) ?? ""));
                items.Add(new SummaryItem(Cell(rows, 0, table.PrimaryNameAttribute), Cell(rows, 0, "createdon"), table.LogicalName, RowId(rows, 0)));
            }
            if (table.FindAttribute("prioritycode") is not null)
            {
                var group = new XElement("entity", new XAttribute("name", table.LogicalName),
                    new XElement("attribute", new XAttribute("name", "prioritycode"), new XAttribute("groupby", "true"), new XAttribute("alias", "p")),
                    new XElement("attribute", new XAttribute("name", table.PrimaryIdAttribute), new XAttribute("aggregate", "count"), new XAttribute("alias", "n")),
                    new XElement("filter", AllConditions(t, id)));
                var groups = await RunQueryAsync(table, Fetch(group, aggregate: true), caller, ct);
                for (var i = 0; i < groups.Rows.Count; i++)
                {
                    var label = Cell(groups, i, "p");
                    metrics.Add(new SummaryMetric("priority:" + label, label.Length == 0 ? "No priority" : label, Cell(groups, i, "n")));
                }
            }
        }
        return Section(SummaryKinds.Cases, table, null, metrics, items, forceOk: count.Count > 0);
    }

    private async Task<SummarySection> ContractsAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var table = t.Table;
        var count = await CountAsync(caller, t, id, null, ct);
        var metrics = new List<SummaryMetric> { new SummaryMetric("count", "Active contracts", count.Count.ToString(CultureInfo.InvariantCulture)) };
        var items = new List<SummaryItem>();
        if (count.Count > 0 && table.FindAttribute("expireson") is not null)
        {
            var today = _clock().UtcDateTime.Date;
            var window = new[]
            {
                Condition("expireson", "on-or-after", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Condition("expireson", "on-or-before", today.AddDays(ExpiryDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            };
            var soon = await CountAsync(caller, t, id, null, ct, window);
            metrics.Add(new SummaryMetric("expiring", $"Ending within {ExpiryDays} days", soon.Count.ToString(CultureInfo.InvariantCulture)));
            if (soon.Count > 0)
            {
                var list = ListEntity(t, id);
                list.Element("filter")!.Add(window);
                var rows = await RunQueryAsync(table, Fetch(list, top: 3), caller, ct);
                for (var i = 0; i < rows.Rows.Count; i++)
                {
                    items.Add(new SummaryItem(Cell(rows, i, table.PrimaryNameAttribute), Cell(rows, i, "expireson"), table.LogicalName, RowId(rows, i)));
                }
            }
        }
        return Section(SummaryKinds.Contracts, table, null, metrics, items, forceOk: count.Count > 0);
    }

    private async Task<SummarySection> OrdersAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var count = await CountAsync(caller, t, id, null, ct);
        var metrics = new List<SummaryMetric> { new SummaryMetric("count", "Open orders", count.Count.ToString(CultureInfo.InvariantCulture)) };
        return Section(SummaryKinds.Orders, t.Table, null, metrics, Array.Empty<SummaryItem>(), forceOk: count.Count > 0);
    }

    private async Task<SummarySection> GenericAsync(Caller caller, Target t, Guid id, CancellationToken ct)
    {
        var table = t.Table;
        var count = await CountAsync(caller, t, id, null, ct);
        var metrics = new List<SummaryMetric> { new SummaryMetric("count", table.Label, count.Count.ToString(CultureInfo.InvariantCulture)) };
        var items = new List<SummaryItem>();
        if (count.Count > 0)
        {
            var rows = await RunQueryAsync(table, Fetch(ListEntity(t, id), top: 3), caller, ct);
            for (var i = 0; i < rows.Rows.Count; i++)
            {
                items.Add(new SummaryItem(Cell(rows, i, table.PrimaryNameAttribute), Cell(rows, i, "createdon"), table.LogicalName, RowId(rows, i)));
            }
        }
        return Section(SummaryKinds.Generic, table, t.Via, metrics, items, forceOk: count.Count > 0);
    }

    private async Task<SummarySection?> RunAsync(SemaphoreSlim gate, string kind, string entity, string title, EntityInfo? via, Func<CancellationToken, Task<SummarySection>> job, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var limited = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limited.CancelAfter(_sectionTimeout());
            try
            {
                return await job(limited.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return SummarySection.Unavailable(kind, entity, title) with { ViaEntity = via?.LogicalName, Via = via?.Label };
            }
            catch (CrmException ex) when (ex.IsPermissionDenied)
            {
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Summary section {Kind} ({Entity}) failed", kind, entity);
                return SummarySection.Unavailable(kind, entity, title) with { ViaEntity = via?.LogicalName, Via = via?.Label };
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static SummarySection Section(string kind, EntityInfo table, EntityInfo? via, IReadOnlyList<SummaryMetric> metrics, IReadOnlyList<SummaryItem> items, bool forceOk = false)
        => new SummarySection(kind, table.LogicalName, table.Label, forceOk || items.Count > 0 ? SummaryStatus.Ok : SummaryStatus.Empty, metrics, items) { ViaEntity = via?.LogicalName, Via = via?.Label };

    private Task<ResultTable> RunQueryAsync(EntityInfo table, XElement fetch, Caller caller, CancellationToken ct)
        => _executor.ExecuteAsync(table.EntitySetName, fetch.ToString(SaveOptions.DisableFormatting), caller, ct);

    private static XElement Fetch(XElement entity, int? top = null, bool aggregate = false)
    {
        var fetch = new XElement("fetch");
        if (aggregate) { fetch.Add(new XAttribute("aggregate", "true")); }
        if (top is int n) { fetch.Add(new XAttribute("top", n)); }
        fetch.Add(entity);
        return fetch;
    }

    private static XElement Condition(string attribute, string op, string? value)
    {
        var c = new XElement("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op));
        if (value is not null) { c.Add(new XAttribute("value", value)); }
        return c;
    }

    private sealed record Counted(long Count, string? Sum);

    private async Task<Counted> CountAsync(Caller caller, Target t, Guid id, string? sumAttribute, CancellationToken ct, IEnumerable<XElement>? extra = null)
    {
        var table = t.Table;
        var entity = new XElement("entity", new XAttribute("name", table.LogicalName),
            new XElement("attribute", new XAttribute("name", table.PrimaryIdAttribute), new XAttribute("aggregate", "count"), new XAttribute("alias", "n")));
        if (sumAttribute is not null)
        {
            entity.Add(new XElement("attribute", new XAttribute("name", sumAttribute), new XAttribute("aggregate", "sum"), new XAttribute("alias", "s")));
        }
        var countConditions = AllConditions(t, id).Concat(extra ?? Array.Empty<XElement>()).ToList();
        if (countConditions.Count > 0)
        {
            entity.Add(new XElement("filter", countConditions));
        }
        if (LinkElement(t, id) is { } link) { entity.Add(link); }
        var rows = await RunQueryAsync(table, Fetch(entity, aggregate: true), caller, ct);
        if (rows.Rows.Count == 0)
        {
            return new Counted(0, null);
        }
        var n = long.TryParse(Digits(Cell(rows, 0, "n")), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var sum = sumAttribute is null ? null : Cell(rows, 0, "s");
        return new Counted(n, string.IsNullOrWhiteSpace(sum) ? null : sum);
    }

    private static string Digits(string text) => new string(text.Where(char.IsDigit).ToArray());

    private static string Cell(ResultTable table, int row, string column)
    {
        var index = -1;
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (string.Equals(table.Columns[i], column, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        }
        return index >= 0 && row < table.Rows.Count && index < table.Rows[row].Count ? table.Rows[row][index] ?? "" : "";
    }

    private static string? RowId(ResultTable table, int row) => table.RowIds is not null && row < table.RowIds.Count ? table.RowIds[row] : null;

    private static string? Join(params string[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }
}
