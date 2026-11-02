using System.Text.RegularExpressions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Defines the allowlist for reads made with the service account without impersonation, so it cannot read business data on its own behalf.
/// </summary>
public static class ServiceReadPolicy
{
    private static readonly Regex EntityDefinitionsPath = new Regex(
        @"^EntityDefinitions(\(LogicalName='[A-Za-z0-9_]+'\)(/Attributes/Microsoft\.Dynamics\.CRM\.[A-Za-z]+AttributeMetadata)?)?$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "$select", "$filter", "$top", "$expand", "$orderby" };
    private static readonly HashSet<string> SystemUserColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "systemuserid", "fullname", "domainname", "isdisabled" };
    private static readonly HashSet<string> TaskColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "activityid", "createdon", "modifiedon", "_createdby_value", "_modifiedby_value", "_createdonbehalfby_value", "_modifiedonbehalfby_value" };
    private static readonly Regex LoginProofFilter = new Regex("^subject eq 'NLQ-login-[A-Za-z0-9_-]{43}'$", RegexOptions.Compiled);
    private static readonly Regex UserAssociationPath = new Regex("^systemusers\\([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\)/(systemuserroles_association|teammembership_association)$", RegexOptions.Compiled);
    private static readonly HashSet<string> NameColumn = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
    private static readonly HashSet<string> ViewColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "layoutxml", "savedqueryid", "name", "returnedtypecode", "isdefault", "querytype" };

    /// <summary>
    /// Throws unless the relative URL is an allowed service read such as metadata, view or user lookup.
    /// </summary>
    public static void EnsureAllowed(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
        {
            throw Refused(relativeUrl);
        }
        var split = relativeUrl.Split('?', 2);
        string path;
        try { path = Uri.UnescapeDataString(split[0]); }
        catch (UriFormatException) { throw Refused(relativeUrl); }
        if (path.Length == 0 || path.StartsWith('/') || path.Contains("..") || path.Contains("//") || path.Contains(':') && !path.StartsWith("EntityDefinitions(LogicalName=", StringComparison.Ordinal))
        {
            throw Refused(relativeUrl);
        }
        var query = ParseQuery(split.Length > 1 ? split[1] : "");
        if (query is null)
        {
            throw Refused(relativeUrl);
        }

        if (path == "WhoAmI")
        {
            if (query.Count > 0) { throw Refused(relativeUrl); }
            return;
        }
        if (EntityDefinitionsPath.IsMatch(path))
        {
            return;
        }
        if (path == "savedqueries")
        {
            RequireNarrow(relativeUrl, query, ViewColumns, allowExpand: false, maxTop: 10, topRequired: true);
            return;
        }
        if (path == "systemusers")
        {
            RequireNarrow(relativeUrl, query, SystemUserColumns, allowExpand: false, maxTop: 100, topRequired: false);
            return;
        }
        if (UserAssociationPath.IsMatch(path))
        {
            RequireNarrow(relativeUrl, query, NameColumn, allowExpand: false, maxTop: 200, topRequired: true);
            if (query.ContainsKey("$filter") || query.ContainsKey("$orderby"))
            {
                throw Refused(relativeUrl);
            }
            return;
        }
        if (path == "tasks")
        {
            RequireNarrow(relativeUrl, query, TaskColumns, allowExpand: false, maxTop: 2, topRequired: true);
            if (query.ContainsKey("$orderby") || !query.TryGetValue("$filter", out var filter) || !LoginProofFilter.IsMatch(filter))
            {
                throw Refused(relativeUrl);
            }
            return;
        }
        throw Refused(relativeUrl);
    }

    private static void RequireNarrow(string url, Dictionary<string, string> query, HashSet<string> columns, bool allowExpand, int maxTop, bool topRequired)
    {
        if (!query.TryGetValue("$select", out var select) || string.IsNullOrWhiteSpace(select))
        {
            throw Refused(url);
        }
        if (select.Split(',').Select(c => c.Trim()).Any(c => !columns.Contains(c)))
        {
            throw Refused(url);
        }
        if (!allowExpand && query.ContainsKey("$expand"))
        {
            throw Refused(url);
        }
        if (query.TryGetValue("$top", out var top))
        {
            if (!int.TryParse(top, out var n) || n < 1 || n > maxTop) { throw Refused(url); }
        }
        else if (topRequired)
        {
            throw Refused(url);
        }
    }

    private static Dictionary<string, string>? ParseQuery(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw.Length == 0)
        {
            return result;
        }
        foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).Trim();
            if (!AllowedQueryKeys.Contains(key) || result.ContainsKey(key))
            {
                return null;
            }
            result[key] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return result;
    }

    private static InvalidOperationException Refused(string? url)
    {
        var shown = (url ?? "").Split('?')[0];
        return new InvalidOperationException("A service-account read is not on the allow list (system reads only: schema, default views, user lookup, WhoAmI, sign-in proof task, role and team names of one user): " + shown);
    }
}
