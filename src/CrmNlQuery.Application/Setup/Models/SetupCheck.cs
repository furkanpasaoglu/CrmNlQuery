namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// One line of a connection check: a stable check id the page translates, its status and an optional short detail that never contains secrets.
/// </summary>
public sealed record SetupCheck(string Id, SetupCheckStatus Status, string? Detail = null);
