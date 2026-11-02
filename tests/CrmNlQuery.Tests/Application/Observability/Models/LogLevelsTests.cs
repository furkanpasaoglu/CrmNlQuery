using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Tests.Application.Observability.Models;

/// <summary>
/// Tests log level name parsing and display names.
/// </summary>
public class LogLevelsTests
{
    /// <summary>
    /// Verifies that parsing understands level names ignoring case.
    /// </summary>
    [Theory]
    [InlineData("Error", LogLevels.Error)]
    [InlineData("warning", LogLevels.Warning)]
    [InlineData(" INFORMATION ", LogLevels.Information)]
    [InlineData("Debug", LogLevels.Debug)]
    [InlineData("Fatal", LogLevels.Fatal)]
    public void Parse_UnderstandsLevelNames_IgnoringCase(string text, int expected)
    {
        Assert.Equal(expected, LogLevels.Parse(text, LogLevels.Verbose));
    }

    /// <summary>
    /// Verifies that parsing falls back to the default for anything else.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("loud")]
    [InlineData("3")]
    public void Parse_FallsBackToTheDefault_ForAnythingElse(string? text)
    {
        Assert.Equal(LogLevels.Warning, LogLevels.Parse(text, LogLevels.Warning));
    }

    /// <summary>
    /// Verifies that the display name is clamped to the known range.
    /// </summary>
    [Theory]
    [InlineData(LogLevels.Verbose, "Verbose")]
    [InlineData(LogLevels.Warning, "Warning")]
    [InlineData(LogLevels.Fatal, "Fatal")]
    [InlineData(99, "Fatal")]
    [InlineData(-5, "Verbose")]
    public void Name_IsTheDisplayName_ClampedToTheKnownRange(int level, string expected)
    {
        Assert.Equal(expected, LogLevels.Name(level));
    }
}
