namespace CrmNlQuery.Application.Common.Exceptions;

/// <summary>
/// Signals that the CRM was unreachable or returned an HTTP error.
/// </summary>
public sealed class CrmException : Exception
{
    public int? StatusCode { get; }
    public bool IsPermissionDenied { get; }
    public bool IsImpersonationDenied { get; }
    public bool IsAggregateLimit { get; }
    public CrmException(string message, int? statusCode = null, Exception? inner = null, bool isPermissionDenied = false, bool isImpersonationDenied = false, bool isAggregateLimit = false) : base(message, inner)
    {
        IsAggregateLimit = isAggregateLimit;
        StatusCode = statusCode;
        IsPermissionDenied = isPermissionDenied;
        IsImpersonationDenied = isImpersonationDenied;
    }
}
