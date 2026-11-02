namespace CrmNlQuery.Web.Security;

/// <summary>
/// Rejects cross-site browser requests, because Windows credentials are sent automatically like a cookie.
/// </summary>
public static class CrossSiteGuard
{
    /// <summary>
    /// Decides whether a request is allowed based on its method, path and site relationship.
    /// </summary>
    public static bool IsAllowed(string method, string path, string? secFetchSite)
    {
        return IsAllowed(method, path, secFetchSite, null, null);
    }

    /// <summary>
    /// Decides whether a request is allowed, also checking the Origin header against the allowed CRM origins for chat write endpoints.
    /// </summary>
    public static bool IsAllowed(string method, string path, string? secFetchSite, string? origin, IReadOnlyList<string>? allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(secFetchSite))
        {
            return true;
        }
        var sameOrigin = secFetchSite == "same-origin" || secFetchSite == "none";
        if (sameOrigin)
        {
            return true;
        }

        if (IsAdminPath(path))
        {
            return false;
        }

        var safeMethod = method == "GET" || method == "HEAD" || method == "OPTIONS";
        if (safeMethod || path.Equals("/api/ask", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (IsWidgetWritePath(path))
        {
            var normalized = (origin ?? "").Trim().ToLowerInvariant();
            return normalized.Length > 0 && allowedOrigins is not null && allowedOrigins.Contains(normalized);
        }
        return false;
    }

    private static bool IsAdminPath(string path)
    {
        return path.Equals("/api/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWidgetWritePath(string path)
    {
        return path.Equals("/api/chats", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/chats/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/me/preferences", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/suggestions/click", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/reports/click", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/create/", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/api/result/", StringComparison.OrdinalIgnoreCase)
                && (path.EndsWith("/open-in-crm", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/explain/edit", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/suggest/apply", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/quality/suggest", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/feedback", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/quality/save", StringComparison.OrdinalIgnoreCase)));
    }
}
