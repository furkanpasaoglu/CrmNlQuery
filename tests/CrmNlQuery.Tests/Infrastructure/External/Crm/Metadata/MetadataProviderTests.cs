using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Tests the metadata provider's caching, expiry, refresh and error behaviour.
/// </summary>
public class MetadataProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "crmnl-test-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private const string ListJson = """{"value":[{"LogicalName":"product","EntitySetName":"products","IsPrivate":false,"DisplayName":{"UserLocalizedLabel":{"Label":"Product"}}}]}""";
    private const string EntityJson = """{"LogicalName":"product","EntitySetName":"products","PrimaryIdAttribute":"productid","PrimaryNameAttribute":"name","DisplayName":{"UserLocalizedLabel":{"Label":"Product"}},"Attributes":[{"LogicalName":"statecode","AttributeType":"State","DisplayName":{"UserLocalizedLabel":{"Label":"Status"}}}],"ManyToOneRelationships":[],"OneToManyRelationships":[]}""";
    private const string OptionsJson = """{"value":[{"LogicalName":"statecode","OptionSet":{"Options":[{"Value":0,"Label":{"UserLocalizedLabel":{"Label":"Active"}}},{"Value":1,"Label":{"UserLocalizedLabel":{"Label":"Retired"}}}]}}]}""";

    private (MetadataProvider Provider, FakeHandler Handler) Create(string? cacheDir = null)
    {
        var handler = new FakeHandler(req =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.StateAttributeMetadata")) return FakeHandler.Json(OptionsJson);
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.")) return FakeHandler.Json("""{"value":[]}""");
            if (url.Contains("EntityDefinitions(LogicalName='product')")) return FakeHandler.Json(EntityJson);
            if (url.Contains("EntityDefinitions(LogicalName='widget')")) return FakeHandler.Json("""{"error":{"message":"not found"}}""", System.Net.HttpStatusCode.NotFound);
            if (url.Contains("EntityDefinitions?")) return FakeHandler.Json(ListJson);
            return FakeHandler.Json("{}", System.Net.HttpStatusCode.NotFound);
        });
        var options = new CrmOptions { BaseUrl = "http://crm/test" };
        var client = new CrmClient(new HttpClient(handler), options);
        return (new MetadataProvider(client, cacheDir ?? _dir, () => _now), handler);
    }

    /// <summary>
    /// Verifies that a cache file of the wrong shape is treated as a miss and refetched.
    /// </summary>
    [Fact]
    public async Task WrongShapeCacheFile_IsTreatedAsMiss_AndRefetched()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "entity-product.json"), "[]");
        var (provider, handler) = Create();

        var entity = await provider.GetEntityAsync("product");

        Assert.Equal("products", entity!.EntitySetName);
        Assert.NotEmpty(handler.Urls);
    }

    /// <summary>
    /// Verifies that an unwritable cache directory does not break the request.
    /// </summary>
    [Fact]
    public async Task UnwritableCacheDirectory_DoesNotBreakTheRequest()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "crmnl-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "I am a file, not a directory");
        try
        {
            var (provider, _) = Create(blocker);
            var entity = await provider.GetEntityAsync("product");
            Assert.Equal("products", entity!.EntitySetName);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    /// Verifies that GetEntity returns an entity with options and caches it on disk.
    /// </summary>
    [Fact]
    public async Task GetEntity_ReturnsEntityWithOptions_AndCachesOnDisk()
    {
        var (provider, handler) = Create();
        var first = await provider.GetEntityAsync("product");
        Assert.Equal(2, first!.FindAttribute("statecode")!.Options.Count);
        var requestsAfterFirst = handler.Urls.Count;

        var second = await provider.GetEntityAsync("product");
        Assert.Equal("products", second!.EntitySetName);
        Assert.Equal(requestsAfterFirst, handler.Urls.Count);
    }

    /// <summary>
    /// Verifies that the cache expires after 24 hours.
    /// </summary>
    [Fact]
    public async Task Cache_ExpiresAfter24Hours()
    {
        var (provider, handler) = Create();
        await provider.GetEntityAsync("product");
        var before = handler.Urls.Count;

        _now = _now.AddHours(25);
        await provider.GetEntityAsync("product");
        Assert.True(handler.Urls.Count > before);
    }

    /// <summary>
    /// Verifies that Refresh clears the cache.
    /// </summary>
    [Fact]
    public async Task Refresh_ClearsCache()
    {
        var (provider, handler) = Create();
        await provider.GetEntityAsync("product");
        var before = handler.Urls.Count;

        await provider.RefreshAsync();
        await provider.GetEntityAsync("product");
        Assert.True(handler.Urls.Count > before);
    }

    /// <summary>
    /// Verifies that an unknown entity returns null.
    /// </summary>
    [Fact]
    public async Task UnknownEntity_ReturnsNull()
    {
        var (provider, _) = Create();
        Assert.Null(await provider.GetEntityAsync("widget"));
    }

    /// <summary>
    /// Verifies that invalid entity names are rejected.
    /// </summary>
    [Theory]
    [InlineData("../../evil")]
    [InlineData("Product Name")]
    [InlineData("")]
    public async Task InvalidEntityName_ReturnsNull_WithoutCallingCrm(string name)
    {
        var (provider, handler) = Create();
        Assert.Null(await provider.GetEntityAsync(name));
        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that the entity list is cached.
    /// </summary>
    [Fact]
    public async Task ListEntities_IsCached()
    {
        var (provider, handler) = Create();
        var list = await provider.ListEntitiesAsync();
        Assert.Single(list);
        var before = handler.Urls.Count;
        await provider.ListEntitiesAsync();
        Assert.Equal(before, handler.Urls.Count);
    }

    /// <summary>
    /// Verifies that a configured time-to-live is used and may be shorter than the default.
    /// </summary>
    [Fact]
    public async Task ConfiguredTtl_IsUsed_AndShorterThanTheDefault()
    {
        var handler = new FakeHandler(req => FakeHandler.Json(ListJson));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        var provider = new MetadataProvider(client, _dir, () => _now, () => TimeSpan.FromMinutes(15));
        await provider.ListEntitiesAsync();
        var before = handler.Urls.Count;

        _now = _now.AddMinutes(10);
        await provider.ListEntitiesAsync();
        Assert.Equal(before, handler.Urls.Count);

        _now = _now.AddMinutes(6);
        await provider.ListEntitiesAsync();
        Assert.True(handler.Urls.Count > before);
    }

    /// <summary>
    /// Verifies that when the CRM fails an expired cache is still used.
    /// </summary>
    [Fact]
    public async Task WhenCrmFails_ExpiredCacheIsStillUsed()
    {
        var fail = false;
        var handler = new FakeHandler(req => fail
            ? FakeHandler.Json("{}", System.Net.HttpStatusCode.InternalServerError)
            : FakeHandler.Json(ListJson));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        var provider = new MetadataProvider(client, _dir, () => _now, () => TimeSpan.FromMinutes(15));
        await provider.ListEntitiesAsync();

        _now = _now.AddHours(1);
        fail = true;
        var list = await provider.ListEntitiesAsync();

        Assert.Single(list);
    }

    /// <summary>
    /// Verifies that when the CRM fails and nothing is cached the error surfaces.
    /// </summary>
    [Fact]
    public async Task WhenCrmFailsAndNothingIsCached_TheErrorSurfaces()
    {
        var handler = new FakeHandler(req => FakeHandler.Json("{}", System.Net.HttpStatusCode.InternalServerError));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        var provider = new MetadataProvider(client, _dir, () => _now);

        await Assert.ThrowsAsync<CrmException>(() => provider.ListEntitiesAsync());
    }

    /// <summary>
    /// Cleans up the temporary cache directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
