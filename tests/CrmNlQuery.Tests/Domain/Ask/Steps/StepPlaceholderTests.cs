using CrmNlQuery.Domain.Ask.Steps;

namespace CrmNlQuery.Tests.Domain.Ask.Steps;

/// <summary>
/// Unit tests for step placeholder parsing and validation in dependent multi-step plans.
/// </summary>
public class StepPlaceholderTests
{
    /// <summary>
    /// Verifies that a well formed placeholder is recognised.
    /// </summary>
    [Theory]
    [InlineData("{{step1}}", 1)]
    [InlineData("{{step2}}", 2)]
    [InlineData(" {{step3}} ", 3)]
    public void AWellFormedPlaceholder_IsRecognised(string text, int step)
    {
        Assert.True(StepPlaceholder.TryParse(text, out var n));
        Assert.Equal(step, n);
        Assert.True(StepPlaceholder.IsPlaceholder(text));
    }

    /// <summary>
    /// Verifies that anything else is not a placeholder.
    /// </summary>
    [Theory]
    [InlineData("{{step}}")]
    [InlineData("{{step0}}")]
    [InlineData("{{step1}}x")]
    [InlineData("step1")]
    [InlineData("{PRODUCT_GUID}")]
    [InlineData("37870e9c-c7c7-f111-b4df-00155d01f303")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNotAPlaceholder(string? text)
    {
        Assert.False(StepPlaceholder.IsPlaceholder(text));
    }

    /// <summary>
    /// Verifies that format round trips.
    /// </summary>
    [Fact]
    public void Format_RoundTrips()
    {
        Assert.Equal("{{step2}}", StepPlaceholder.Format(2));
        Assert.True(StepPlaceholder.TryParse(StepPlaceholder.Format(2), out var n));
        Assert.Equal(2, n);
    }
}
