using System.Globalization;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Drafts.Models;
using CrmNlQuery.Application.Summary.Models;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Drafts.Services;

/// <summary>
/// Reads placeholder values and the recipient address for an email draft on behalf of the caller using the filtered schema.
/// </summary>
public sealed class DraftValueReader
{
    private readonly IMetadataSource _metadata;
    private readonly IQueryExecutor _executor;
    private readonly SummaryBuilder _summary;
    private readonly Func<DateTimeOffset> _clock;

    public DraftValueReader(IMetadataSource metadata, IQueryExecutor executor, SummaryBuilder summary, Func<DateTimeOffset>? clock = null)
    {
        _metadata = metadata;
        _executor = executor;
        _summary = summary;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Holds the placeholder values and recipient read for a draft.
    /// </summary>
    public sealed record Data(IReadOnlyDictionary<string, string> Values, string? To);

    /// <summary>
    /// Reads the draft placeholder values and recipient for a record as the caller.
    /// </summary>
    public async Task<Data> ReadAsync(Caller caller, string entity, Guid id, string name, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? to = null;
        if (string.Equals(entity, "account", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
        {
            values[DraftPlaceholders.CustomerName] = name;
        }

        var contact = await ReadContactAsync(caller, entity, id, ct);
        if (contact is not null)
        {
            Put(values, DraftPlaceholders.ContactFirstName, contact.Get("firstname"));
            Put(values, DraftPlaceholders.ContactFullName, contact.Get("fullname"));
            to = Clean(contact.Get("emailaddress1"));
            if (string.Equals(entity, "contact", StringComparison.OrdinalIgnoreCase))
            {
                Put(values, DraftPlaceholders.CustomerName, contact.Get("parentcustomerid"));
            }
        }

        var summary = await _summary.BuildAsync(caller, entity, id, name, ct, knownOnly: true);
        foreach (var section in summary.Sections.Where(s => s.Status != SummaryStatus.Unavailable))
        {
            switch (section.Kind)
            {
                case SummaryKinds.Opportunities when Count(section) > 0:
                    Put(values, DraftPlaceholders.OpenOpportunityCount, Metric(section, "count"));
                    Put(values, DraftPlaceholders.OpenOpportunityTotal, Metric(section, "total"));
                    break;
                case SummaryKinds.Cases when Count(section) > 0:
                    Put(values, DraftPlaceholders.OpenCaseCount, Metric(section, "count"));
                    break;
                case SummaryKinds.Contracts when section.Items.Count > 0:
                    Put(values, DraftPlaceholders.NearestContractEnd, section.Items[0].Detail);
                    break;
                case SummaryKinds.Activities:
                    var last = Metric(section, "last_contact");
                    if (!string.Equals(last, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        Put(values, DraftPlaceholders.LastActivityDate, last);
                    }
                    break;
            }
        }
        values[DraftPlaceholders.Today] = _clock().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new Data(values, to);
    }

    /// <summary>
    /// Reads the record name when reopening a draft, returning empty if the record is no longer readable, with the caller permissions.
    /// </summary>
    public async Task<string> ReadNameAsync(Caller caller, string entity, Guid id, CancellationToken ct = default)
    {
        var info = await _metadata.GetEntityAsync(entity, ct);
        if (info is null)
        {
            return "";
        }
        try
        {
            var element = new XElement("entity", new XAttribute("name", info.LogicalName), new XElement("attribute", new XAttribute("name", info.PrimaryNameAttribute)),
                new XElement("filter", new XElement("condition", new XAttribute("attribute", info.PrimaryIdAttribute), new XAttribute("operator", "eq"), new XAttribute("value", id.ToString()))));
            var table = await _executor.ExecuteAsync(info.EntitySetName, new XElement("fetch", new XAttribute("top", 1), element).ToString(SaveOptions.DisableFormatting), caller, ct);
            return table.Rows.Count > 0 && table.Rows[0].Count > 0 ? table.Rows[0][0] ?? "" : "";
        }
        catch (CrmException)
        {
            return "";
        }
    }

    private static long Count(SummarySection section)
        => long.TryParse(Metric(section, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string? Metric(SummarySection section, string key) => section.Metrics.FirstOrDefault(m => m.Key == key)?.Value;

    private static void Put(Dictionary<string, string> values, string name, string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is not null)
        {
            values[name] = cleaned;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Row(IReadOnlyList<string> Columns, IReadOnlyList<string> Cells)
    {
        /// <summary>
        /// Returns the value of a column from a result row, or null.
        /// </summary>
        public string? Get(string column)
        {
            for (var i = 0; i < Columns.Count; i++)
            {
                if (string.Equals(Columns[i], column, StringComparison.OrdinalIgnoreCase))
                {
                    return i < Cells.Count ? Cells[i] : null;
                }
            }
            return null;
        }
    }

    private async Task<Row?> ReadContactAsync(Caller caller, string entity, Guid id, CancellationToken ct)
    {
        var contact = await _metadata.GetEntityAsync("contact", ct);
        if (contact is null)
        {
            return null;
        }
        var columns = new[] { "firstname", "fullname", "emailaddress1", "parentcustomerid" }.Where(c => contact.FindAttribute(c) is not null).ToList();
        if (columns.Count == 0)
        {
            return null;
        }
        var element = new XElement("entity", new XAttribute("name", contact.LogicalName));
        foreach (var column in columns)
        {
            element.Add(new XElement("attribute", new XAttribute("name", column)));
        }

        if (string.Equals(entity, "contact", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(new XElement("filter", new XElement("condition", new XAttribute("attribute", contact.PrimaryIdAttribute), new XAttribute("operator", "eq"), new XAttribute("value", id.ToString()))));
        }
        else if (string.Equals(entity, "account", StringComparison.OrdinalIgnoreCase))
        {
            var account = await _metadata.GetEntityAsync("account", ct);
            if (account is null || account.FindAttribute("primarycontactid") is null)
            {
                return null;
            }
            element.Add(new XElement("link-entity", new XAttribute("name", "account"), new XAttribute("from", "primarycontactid"), new XAttribute("to", contact.PrimaryIdAttribute), new XAttribute("alias", "pc"), new XAttribute("link-type", "inner"),
                new XElement("filter", new XElement("condition", new XAttribute("attribute", account.PrimaryIdAttribute), new XAttribute("operator", "eq"), new XAttribute("value", id.ToString())))));
        }
        else
        {
            return null;
        }

        try
        {
            var table = await _executor.ExecuteAsync(contact.EntitySetName, new XElement("fetch", new XAttribute("top", 1), element).ToString(SaveOptions.DisableFormatting), caller, ct);
            return table.Rows.Count == 0 ? null : new Row(table.Columns, table.Rows[0].Select(c => c ?? "").ToList());
        }
        catch (CrmException)
        {
            return null;
        }
    }
}
