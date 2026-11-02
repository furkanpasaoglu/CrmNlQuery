using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Fake CRM handler that mimics the userqueries personal view API for tests.
/// </summary>
public sealed class FakeCrmUserQueries : HttpMessageHandler
{
    /// <summary>
    /// Represents a stored fake personal view with id, owner, name, entity, query and layout.
    /// </summary>
    public sealed class View
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
        public string Name { get; set; } = "";
        public string Entity { get; set; } = "";
        public string FetchXml { get; set; } = "";
        public string LayoutXml { get; set; } = "";
        public bool Shared { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
    }

    private readonly object _gate = new object();
    private DateTimeOffset _clock = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    public List<View> Views { get; } = new List<View>();
    public List<string> Requests { get; } = new List<string>();
    public Dictionary<string, int> TypeCodes { get; } = new Dictionary<string, int> { ["account"] = 1, ["product"] = 1024, ["contract"] = 1010 };
    public string? FailNextWriteWith { get; set; }

    /// <summary>
    /// Adds a fake personal view for an owner and returns it.
    /// </summary>
    public View Add(Guid owner, string name, string entity, bool shared = false)
    {
        lock (_gate)
        {
            _clock = _clock.AddSeconds(1);
            var view = new View { Id = Guid.NewGuid(), Owner = owner, Name = name, Entity = entity, Shared = shared, CreatedOn = _clock };
            Views.Add(view);
            return view;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            Requests.Add(request.Method + " " + url);
            var caller = request.Headers.TryGetValues("MSCRMCallerID", out var values) ? Guid.Parse(values.Single()) : Guid.Empty;
            var body = request.Content is null ? "" : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(Handle(request.Method, url, caller, body));
        }
    }

    private HttpResponseMessage Handle(HttpMethod method, string url, Guid caller, string body)
    {
        var typeMatch = Regex.Match(url, @"EntityDefinitions\(LogicalName='(?<n>[^']+)'\)");
        if (method == HttpMethod.Get && typeMatch.Success)
        {
            return TypeCodes.TryGetValue(typeMatch.Groups["n"].Value, out var code)
                ? Json("{\"ObjectTypeCode\":" + code + "}")
                : Json("{\"error\":{\"message\":\"unknown entity\"}}", HttpStatusCode.NotFound);
        }

        if (method != HttpMethod.Get && FailNextWriteWith is not null)
        {
            var text = FailNextWriteWith;
            FailNextWriteWith = null;
            return Json("{\"error\":{\"message\":\"" + text + "\"}}", HttpStatusCode.Forbidden);
        }

        if (method == HttpMethod.Get && url.Contains("/userqueries?"))
        {
            IEnumerable<View> visible = Views.Where(v => v.Owner == caller || v.Shared);
            var name = Regex.Match(url, @"name eq '(?<n>(?:[^']|'')*)'");
            if (name.Success)
            {
                var wanted = name.Groups["n"].Value.Replace("''", "'");
                visible = visible.Where(v => v.Name == wanted);
            }
            var owner = Regex.Match(url, @"_ownerid_value eq (?<g>[0-9a-fA-F-]{36})");
            if (owner.Success)
            {
                var wantedOwner = Guid.Parse(owner.Groups["g"].Value);
                visible = visible.Where(v => v.Owner == wantedOwner);
            }
            var items = visible.OrderBy(v => v.CreatedOn).Select(v => new Dictionary<string, object>
            {
                ["userqueryid"] = v.Id.ToString("D"),
                ["returnedtypecode"] = v.Entity,
                ["createdon"] = v.CreatedOn.ToString("o"),
            }).ToList();
            return Json(JsonSerializer.Serialize(new { value = items }));
        }

        if (method == HttpMethod.Post && url.EndsWith("/userqueries"))
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var view = Add(caller, root.GetProperty("name").GetString()!, root.GetProperty("returnedtypecode").GetString()!);
            view.FetchXml = root.GetProperty("fetchxml").GetString()!;
            view.LayoutXml = root.GetProperty("layoutxml").GetString()!;
            return Json(JsonSerializer.Serialize(new { userqueryid = view.Id.ToString("D"), name = view.Name }), HttpStatusCode.Created);
        }

        var idMatch = Regex.Match(url, @"/userqueries\((?<g>[0-9a-fA-F-]{36})\)");
        if (idMatch.Success)
        {
            var view = Views.FirstOrDefault(v => v.Id == Guid.Parse(idMatch.Groups["g"].Value));
            if (view is null || view.Owner != caller)
            {
                return Json("{\"error\":{\"message\":\"Does Not Exist\"}}", HttpStatusCode.NotFound);
            }
            if (method == HttpMethod.Delete)
            {
                Views.Remove(view);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (method == HttpMethod.Patch)
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("fetchxml", out var fetch)) { view.FetchXml = fetch.GetString()!; }
                if (doc.RootElement.TryGetProperty("layoutxml", out var layout)) { view.LayoutXml = layout.GetString()!; }
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        return Json("{\"error\":{\"message\":\"not handled: " + method + " " + url + "\"}}", HttpStatusCode.BadRequest);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
