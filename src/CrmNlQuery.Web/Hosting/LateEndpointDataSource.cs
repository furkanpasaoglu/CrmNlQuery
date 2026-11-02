using Microsoft.Extensions.Primitives;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Endpoint data source whose endpoints are added once services are ready, after the web server may already be running.
/// </summary>
public sealed class LateEndpointDataSource : EndpointDataSource
{
    private readonly object _gate = new object();
    private IReadOnlyList<Endpoint> _endpoints = Array.Empty<Endpoint>();
    private CancellationTokenSource _changed = new CancellationTokenSource();
    private IChangeToken _token;

    public LateEndpointDataSource()
    {
        _token = new CancellationChangeToken(_changed.Token);
    }

    public override IReadOnlyList<Endpoint> Endpoints
    {
        get { lock (_gate) { return _endpoints; } }
    }

    /// <summary>
    /// Returns the change token that signals endpoint updates.
    /// </summary>
    public override IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return _token;
        }
    }

    /// <summary>
    /// Sets the endpoints and signals that the data source changed.
    /// </summary>
    public void Set(IReadOnlyList<Endpoint> endpoints)
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            _endpoints = endpoints;
            previous = _changed;
            _changed = new CancellationTokenSource();
            _token = new CancellationChangeToken(_changed.Token);
        }
        previous.Cancel();
        previous.Dispose();
    }
}
