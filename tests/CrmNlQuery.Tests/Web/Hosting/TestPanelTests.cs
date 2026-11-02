using CrmNlQuery.Web.Hosting;

namespace CrmNlQuery.Tests.Web.Hosting;

/// <summary>
/// Holds tests for the test panel availability.
/// </summary>
public class TestPanelTests
{
    /// <summary>
    /// Verifies that the panel is on in development only unless the setting says otherwise.
    /// </summary>
    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("true", false, true)]
    [InlineData("TRUE", false, true)]
    [InlineData("false", true, false)]
    [InlineData("nonsense", true, true)]
    [InlineData("nonsense", false, false)]
    public void ThePanel_IsOnInDevelopmentOnly_UnlessTheSettingSaysOtherwise(string? setting, bool isDevelopment, bool expected)
    {
        Assert.Equal(expected, TestPanel.IsEnabled(setting, isDevelopment));
    }

    /// <summary>
    /// Verifies that only the root and the index page are the test panel.
    /// </summary>
    [Theory]
    [InlineData("/", true)]
    [InlineData("/index.html", true)]
    [InlineData("/INDEX.HTML", true)]
    [InlineData("/history.html", false)]
    [InlineData("/admin.html", false)]
    [InlineData("/result.html", false)]
    [InlineData("/api/ask", false)]
    [InlineData("/widget/widget.js", false)]
    public void OnlyTheRootAndTheIndexPage_AreTheTestPanel(string path, bool expected)
    {
        Assert.Equal(expected, TestPanel.IsTestPanelPath(path));
    }
}
