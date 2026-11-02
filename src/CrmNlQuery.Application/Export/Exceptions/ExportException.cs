namespace CrmNlQuery.Application.Export.Exceptions;

/// <summary>
/// Signals a refused or failed export with a code of invalid, forbidden, busy or unavailable.
/// </summary>
public sealed class ExportException : Exception
{
    public string Code { get; }

    public ExportException(string code, string message) : base(message)
    {
        Code = code;
    }
}
