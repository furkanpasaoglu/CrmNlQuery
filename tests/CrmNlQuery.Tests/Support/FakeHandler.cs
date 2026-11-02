using System.Net;
using System.Text;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Fake HTTP handler used in tests to return canned responses.
/// </summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<string> Urls { get; } = new List<string>();
    public List<string> Bodies { get; } = new List<string>();

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    /// <summary>
    /// Builds a JSON HTTP response with the given status for tests.
    /// </summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Urls.Add(request.RequestUri!.AbsoluteUri);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return _respond(request);
    }
}
