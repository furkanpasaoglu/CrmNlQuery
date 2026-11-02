using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Live tests against a real CRM for metadata reading; they need the lab CRM and are marked Live.
/// </summary>
[Trait("Category", "Live")]
public class LiveMetadataTests
{
    /// <summary>
    /// Handles live options for the tests.
    /// </summary>
    internal static CrmOptions LiveOptions() => new CrmOptions
    {
        BaseUrl = Environment.GetEnvironmentVariable("Crm__BaseUrl") ?? "http://crm.example.com/contoso",
        Domain = Environment.GetEnvironmentVariable("Crm__Domain") ?? "CONTOSO",
        Username = Environment.GetEnvironmentVariable("Crm__Username") ?? "Administrator",
        Password = Environment.GetEnvironmentVariable("Crm__Password") ?? "",
        MetadataCacheDir = Path.Combine(Path.GetTempPath(), "crmnl-live-" + Guid.NewGuid().ToString("N")),
    };

    /// <summary>
    /// Verifies that product has statecode options and account is listed.
    /// </summary>
    [Fact]
    public async Task Product_HasStatecodeOptions_AndAccountIsListed()
    {
        var options = LiveOptions();
        var provider = new MetadataProvider(CrmClient.Create(options), options.MetadataCacheDir, () => DateTimeOffset.UtcNow);

        var list = await provider.ListEntitiesAsync();
        Assert.Contains(list, e => e.LogicalName == "account");

        var product = await provider.GetEntityAsync("product");
        Assert.NotNull(product);
        Assert.Equal("products", product!.EntitySetName);
        Assert.NotEmpty(product.FindAttribute("statecode")!.Options);

        Assert.Null(await provider.GetEntityAsync("definitelynotanentity"));
    }
}
