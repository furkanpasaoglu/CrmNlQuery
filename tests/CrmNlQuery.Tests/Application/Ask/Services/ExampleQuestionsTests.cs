using CrmNlQuery.Application.Ask.Services;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests the configurable example questions shown in the empty chat, cleaning mistyped values.
/// </summary>
public class ExampleQuestionsTests
{
    /// <summary>
    /// Verifies that nothing configured gives an empty list so the widget keeps its built in examples.
    /// </summary>
    [Fact]
    public void NothingConfigured_GivesAnEmptyList_SoTheWidgetKeepsItsBuiltInExamples()
    {
        Assert.Empty(ExampleQuestions.Clean(null));
        Assert.Empty(ExampleQuestions.Clean(new string[0]));
        Assert.Empty(ExampleQuestions.Clean(new[] { "", "   ", "\n" }));
    }

    /// <summary>
    /// Verifies that entries are trimmed, single line, stripped of control characters and limited.
    /// </summary>
    [Fact]
    public void Entries_AreTrimmed_SingleLine_ControlCharactersRemoved_AndLimited()
    {
        var cleaned = ExampleQuestions.Clean(new[] { "  Show open cases  ", "Line one\r\nline two", "bell\u0007char", new string('x', 500) });

        Assert.Equal("Show open cases", cleaned[0]);
        Assert.Equal("Line one line two", cleaned[1]);
        Assert.Equal("bellchar", cleaned[2]);
        Assert.Equal(ExampleQuestions.MaxChars, cleaned[3].Length);
    }

    /// <summary>
    /// Verifies that at most five entries are kept with duplicates dropped and order kept.
    /// </summary>
    [Fact]
    public void AtMostFiveEntries_AreKept_DuplicatesDropped_OrderKept()
    {
        var cleaned = ExampleQuestions.Clean(new[] { "a", "b", "A", "c", "d", "e", "f", "g" });

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, cleaned);
    }
}
