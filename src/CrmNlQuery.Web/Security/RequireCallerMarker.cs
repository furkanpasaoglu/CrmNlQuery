namespace CrmNlQuery.Web.Security;

/// <summary>
/// Marker that tags every /api endpoint and also acts as an endpoint filter that runs the caller-identity gate itself.
/// </summary>
public sealed record RequireCallerMarker;
