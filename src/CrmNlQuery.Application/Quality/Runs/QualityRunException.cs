namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Exception raised when a quality run cannot be started or managed.
/// </summary>
public sealed class QualityRunException : Exception
{
    public string Code { get; }

    public QualityRunException(string code, string message) : base(message)
    {
        Code = code;
    }
}
