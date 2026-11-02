namespace CrmNlQuery.Application.Operations.Startup;

/// <summary>
/// Exception raised for startup dependency conditions.
/// </summary>
public sealed class StartupDependencyException : InvalidOperationException
{
    public StartupDependencyException(string message, bool canRetry, Exception inner)
        : base(message, inner)
    {
        CanRetry = canRetry;
    }

    public bool CanRetry { get; }
}
