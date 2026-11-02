namespace CrmNlQuery.Web.Security;

/// <summary>
/// Resolves the list of origins allowed by CORS, with no wildcard since the service is called only from known CRM addresses.
/// </summary>
public static class CorsOrigins
{
    /// <summary>
    /// Resolves the allowed origins from the configured list, falling back to the CRM address when it is empty.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? configured, string crmBaseUrl)
    {
        var source = string.IsNullOrWhiteSpace(configured) ? crmBaseUrl : configured;
        var result = new List<string>();
        foreach (var part in source.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var origin = ToOrigin(part.Trim());
            if (origin is not null && !result.Contains(origin))
            {
                result.Add(origin);
            }
        }
        return result;
    }

    private static string? ToOrigin(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
