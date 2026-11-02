namespace CrmNlQuery.Application.DataPolicy.Exceptions;

/// <summary>
/// Signals that a stored query now uses closed data so the result is not re-run, with a fixed message and no internal detail.
/// </summary>
public sealed class DataPolicyException : Exception
{
    public static string UserMessage => CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.DataPolicyGone);

    public DataPolicyException() : base(UserMessage)
    {
    }
}
