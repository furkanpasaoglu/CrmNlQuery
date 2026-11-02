namespace CrmNlQuery.Application.Design.Exceptions;

/// <summary>
/// Signals a design job error that is safe to show to the user.
/// </summary>
public sealed class DesignJobException : Exception
{
    public string Code { get; }

    public DesignJobException(string code, string message) : base(message)
    {
        Code = code;
    }
}
