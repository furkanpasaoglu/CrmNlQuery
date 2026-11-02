namespace CrmNlQuery.Web.Security;

/// <summary>
/// Marker for endpoints that are intentionally public, with a stated reason.
/// </summary>
public sealed record PublicMarker(string Reason);
