namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// Result of one check in the setup wizard: fine, fine but worth a warning, or failed.
/// </summary>
public enum SetupCheckStatus
{
    Ok,
    Warn,
    Fail,
}
