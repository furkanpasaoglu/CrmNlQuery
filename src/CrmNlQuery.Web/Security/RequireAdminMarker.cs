namespace CrmNlQuery.Web.Security;

/// <summary>
/// Marker for endpoints that require an administrator permission.
/// </summary>
public sealed record RequireAdminMarker(string? Permission);
