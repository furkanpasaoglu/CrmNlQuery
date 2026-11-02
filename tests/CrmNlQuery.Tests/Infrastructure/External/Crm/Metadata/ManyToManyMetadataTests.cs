using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Tests that many-to-many relationships are read from metadata including the intersect table and the two end attributes.
/// </summary>
public class ManyToManyMetadataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "crmnl-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private const string EntityJson = """
    {"LogicalName":"new_arac","EntitySetName":"new_aracs","PrimaryIdAttribute":"new_aracid","PrimaryNameAttribute":"new_plaka",
     "DisplayName":{"UserLocalizedLabel":{"Label":"Araç"}},
     "Attributes":[{"LogicalName":"new_aracid","AttributeType":"Uniqueidentifier","AttributeTypeName":{"Value":"UniqueidentifierType"},"DisplayName":null}],
     "ManyToOneRelationships":[],"OneToManyRelationships":[],
     "ManyToManyRelationships":[
       {"SchemaName":"new_arac_aksesuar","IntersectEntityName":"new_arac_aksesuar","Entity1LogicalName":"new_arac","Entity1IntersectAttribute":"new_aracid","Entity2LogicalName":"new_aksesuar","Entity2IntersectAttribute":"new_aksesuarid"}]}
    """;

    private (MetadataProvider Provider, FakeHandler Handler) Create(string entityJson = EntityJson)
    {
        var handler = new FakeHandler(req =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.")) return FakeHandler.Json("""{"value":[]}""");
            if (url.Contains("EntityDefinitions(LogicalName='new_arac')")) return FakeHandler.Json(entityJson);
            return FakeHandler.Json("{}", System.Net.HttpStatusCode.NotFound);
        });
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        return (new MetadataProvider(client, _dir, () => _now), handler);
    }

    /// <summary>
    /// Verifies that the definition request asks for the many to many relationships.
    /// </summary>
    [Fact]
    public async Task TheDefinitionRequest_AsksForTheManyToManyRelationships()
    {
        var (provider, handler) = Create();

        await provider.GetEntityAsync("new_arac");

        Assert.Contains(handler.Urls, u => Uri.UnescapeDataString(u).Contains("ManyToManyRelationships($select=SchemaName,IntersectEntityName,Entity1LogicalName,Entity1IntersectAttribute,Entity2LogicalName,Entity2IntersectAttribute)"));
    }

    /// <summary>
    /// Verifies that a many-to-many relationship is parsed with its intersect entity and two attributes.
    /// </summary>
    [Fact]
    public async Task TheManyToManyRelationship_IsParsed_WithTheIntersectEntityAndItsTwoAttributes()
    {
        var (provider, _) = Create();

        var entity = (await provider.GetEntityAsync("new_arac"))!;

        var link = Assert.Single(entity.ManyToMany);
        Assert.Equal("new_arac_aksesuar", link.SchemaName);
        Assert.Equal("new_arac_aksesuar", link.IntersectEntity);
        Assert.Equal("new_arac", link.Entity1);
        Assert.Equal("new_aracid", link.Entity1Attribute);
        Assert.Equal("new_aksesuar", link.Entity2);
        Assert.Equal("new_aksesuarid", link.Entity2Attribute);
    }

    private const string PlainJson = """
    {"LogicalName":"new_arac","EntitySetName":"new_aracs","PrimaryIdAttribute":"new_aracid","PrimaryNameAttribute":"new_plaka",
     "DisplayName":{"UserLocalizedLabel":{"Label":"Araç"}},
     "Attributes":[{"LogicalName":"new_aracid","AttributeType":"Uniqueidentifier","AttributeTypeName":{"Value":"UniqueidentifierType"},"DisplayName":null}],
     "ManyToOneRelationships":[],"OneToManyRelationships":[]}
    """;

    /// <summary>
    /// Verifies that an entity without the property has no many-to-many relationships.
    /// </summary>
    [Fact]
    public async Task AnEntityWithoutTheProperty_HasNoManyToMany()
    {
        var (provider, _) = Create(PlainJson);

        var entity = (await provider.GetEntityAsync("new_arac"))!;

        Assert.Empty(entity.ManyToMany);
    }

    /// <summary>
    /// Verifies that many-to-many relationships survive the disk cache.
    /// </summary>
    [Fact]
    public async Task TheManyToManyRelationships_SurviveTheDiskCache()
    {
        var (provider, handler) = Create();
        await provider.GetEntityAsync("new_arac");
        var requestsAfterFirst = handler.Urls.Count;

        var cached = (await provider.GetEntityAsync("new_arac"))!;

        Assert.Equal(requestsAfterFirst, handler.Urls.Count);
        Assert.Equal("new_aksesuar", Assert.Single(cached.ManyToMany).Entity2);
    }

    /// <summary>
    /// Cleans up the test's temporary resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
