using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Tests.Application.Ask;

/// <summary>
/// Tests that clients which do not report token usage still work through the complete call.
/// </summary>
public class FakeLlmUsageTests
{
    /// <summary>
    /// Verifies that clients that do not report usage still work through complete async.
    /// </summary>
    [Fact]
    public async Task ClientsThatDoNotReportUsage_StillWorkThroughCompleteAsync()
    {
        ILlmClient llm = new FakeLlm("""{"ok":true}""");
        var result = await llm.CompleteAsync("s", "u", "{}");
        Assert.Equal("""{"ok":true}""", result.Text);
        Assert.Null(result.Usage);
    }
}
