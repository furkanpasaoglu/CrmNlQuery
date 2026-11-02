namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Describes the service startup state: starting, failed or ready.
/// </summary>
public enum StartupState
{
    Starting,
    Failed,
    Ready,
}
