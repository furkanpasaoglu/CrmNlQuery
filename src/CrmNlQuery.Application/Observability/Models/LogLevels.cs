namespace CrmNlQuery.Application.Observability.Models;

/// <summary>
/// Maps log level names and numbers using the same numbers as Serilog without depending on it.
/// </summary>
public static class LogLevels
{
    public const int Verbose = 0;
    public const int Debug = 1;
    public const int Information = 2;
    public const int Warning = 3;
    public const int Error = 4;
    public const int Fatal = 5;

    private static readonly string[] Names = { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    /// <summary>
    /// Returns the display name of a log level number.
    /// </summary>
    public static string Name(int level) => Names[Math.Clamp(level, Verbose, Fatal)];

    /// <summary>
    /// Parses a level name, returning the given fallback for unknown or empty text.
    /// </summary>
    public static int Parse(string? text, int fallback)
    {
        var trimmed = (text ?? "").Trim();
        for (var i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return fallback;
    }
}
