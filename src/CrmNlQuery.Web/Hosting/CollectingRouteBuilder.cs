namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Temporary route builder that collects endpoint mappings without writing them to the real application.
/// </summary>
public sealed class CollectingRouteBuilder : IEndpointRouteBuilder
{
    public CollectingRouteBuilder(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

    /// <summary>
    /// Creates an application builder for the collecting route builder.
    /// </summary>
    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);

    /// <summary>
    /// Returns the endpoints gathered so far by the collecting route builder, which the late endpoint data source later serves once startup finishes.
    /// </summary>
    public IReadOnlyList<Endpoint> Collect() => DataSources.SelectMany(source => source.Endpoints).ToList();
}
