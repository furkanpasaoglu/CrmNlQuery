using CrmNlQuery.Web.Security;

namespace CrmNlQuery.Tests.Web.Security;

/// <summary>
/// Unit tests for request size and length limits such as question length and body limits.
/// </summary>
public class RequestLimitsTests
{
    /// <summary>
    /// Verifies that each path gets its own body limit.
    /// </summary>
    [Theory]
    [InlineData("/api/ask", 64 * 1024)]
    [InlineData("/api/result/abc/refine", 64 * 1024)]
    [InlineData("/api/history/abc/star", 64 * 1024)]
    [InlineData("/api/admin/users", 64 * 1024)]
    [InlineData("/api/admin/documents", 256 * 1024)]
    [InlineData("/api/admin/documents/search", 256 * 1024)]
    [InlineData("/API/Admin/Documents/abc", 256 * 1024)]
    [InlineData("/widget/widget.js", 1024 * 1024)]
    [InlineData("/", 1024 * 1024)]
    public void EachPathGetsItsOwnBodyLimit(string path, long expected)
    {
        Assert.Equal(expected, RequestLimits.MaxBytesFor(path));
    }
}
