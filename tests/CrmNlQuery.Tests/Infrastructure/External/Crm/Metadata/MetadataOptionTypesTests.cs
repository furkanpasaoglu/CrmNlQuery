using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Tests option labels for multi-select and yes/no fields so the model knows an option or a boolean exists.
/// </summary>
public class MetadataOptionTypesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "crmnl-test-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private const string EntityJson = """
    {"LogicalName":"new_arac","EntitySetName":"new_aracs","PrimaryIdAttribute":"new_aracid","PrimaryNameAttribute":"new_plaka",
     "DisplayName":{"UserLocalizedLabel":{"Label":"Araç"}},
     "Attributes":[
       {"LogicalName":"new_aracid","AttributeType":"Uniqueidentifier","AttributeTypeName":{"Value":"UniqueidentifierType"},"DisplayName":null},
       {"LogicalName":"new_plaka","AttributeType":"String","AttributeTypeName":{"Value":"StringType"},"DisplayName":{"UserLocalizedLabel":{"Label":"Plaka"}}},
       {"LogicalName":"new_garantili","AttributeType":"Boolean","AttributeTypeName":{"Value":"BooleanType"},"DisplayName":{"UserLocalizedLabel":{"Label":"Garanti devam ediyor"}}},
       {"LogicalName":"new_ozellikler","AttributeType":"Virtual","AttributeTypeName":{"Value":"MultiSelectPicklistType"},"DisplayName":{"UserLocalizedLabel":{"Label":"Özellikler"}}},
       {"LogicalName":"statecodename","AttributeType":"Virtual","AttributeTypeName":{"Value":"VirtualType"},"DisplayName":null}
     ],
     "ManyToOneRelationships":[],"OneToManyRelationships":[]}
    """;

    private const string BooleanJson = """
    {"value":[{"LogicalName":"new_garantili","OptionSet":{"TrueOption":{"Value":1,"Label":{"UserLocalizedLabel":{"Label":"Evet"}}},"FalseOption":{"Value":0,"Label":{"UserLocalizedLabel":{"Label":"Hayır"}}}}}]}
    """;

    private const string MultiJson = """
    {"value":[{"LogicalName":"new_ozellikler","OptionSet":{"Options":[
       {"Value":100000000,"Label":{"UserLocalizedLabel":{"Label":"Sunroof"}}},{"Value":100000001,"Label":{"UserLocalizedLabel":{"Label":"Deri koltuk"}}}]}}]}
    """;

    private (MetadataProvider Provider, FakeHandler Handler) Create(bool multiSelectSupported = true)
    {
        var handler = new FakeHandler(req =>
        {
            var url = Uri.UnescapeDataString(req.RequestUri!.ToString());
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.BooleanAttributeMetadata")) return FakeHandler.Json(BooleanJson);
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata"))
            {
                return multiSelectSupported ? FakeHandler.Json(MultiJson) : FakeHandler.Json("""{"error":{"message":"not supported"}}""", System.Net.HttpStatusCode.BadRequest);
            }
            if (url.Contains("/Attributes/Microsoft.Dynamics.CRM.")) return FakeHandler.Json("""{"value":[]}""");
            if (url.Contains("EntityDefinitions(LogicalName='new_arac')")) return FakeHandler.Json(EntityJson);
            return FakeHandler.Json("{}", System.Net.HttpStatusCode.NotFound);
        });
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });
        return (new MetadataProvider(client, _dir, () => _now), handler);
    }

    /// <summary>
    /// Verifies that a boolean column gets its true and false labels as options.
    /// </summary>
    [Fact]
    public async Task ABooleanColumn_GetsItsTrueAndFalseLabels_AsOptions()
    {
        var (provider, _) = Create();

        var entity = (await provider.GetEntityAsync("new_arac"))!;

        var options = entity.FindAttribute("new_garantili")!.Options;
        Assert.Equal(new[] { (1, "Evet"), (0, "Hayır") }, options.Select(o => (o.Value, o.Label)).ToArray());
    }

    /// <summary>
    /// Verifies that a multi select column is typed as multi select picklist with its option labels.
    /// </summary>
    [Fact]
    public async Task AMultiSelectColumn_IsTypedAsMultiSelectPicklist_WithItsOptionLabels()
    {
        var (provider, _) = Create();

        var entity = (await provider.GetEntityAsync("new_arac"))!;

        var column = entity.FindAttribute("new_ozellikler")!;
        Assert.Equal("MultiSelectPicklist", column.AttributeType);
        Assert.Equal(new[] { (100000000, "Sunroof"), (100000001, "Deri koltuk") }, column.Options.Select(o => (o.Value, o.Label)).ToArray());
        Assert.Equal("Virtual", entity.FindAttribute("statecodename")!.AttributeType);
    }

    /// <summary>
    /// Verifies that an old CRM that does not know multi select still gives the entity.
    /// </summary>
    [Fact]
    public async Task AnOldCrmThatDoesNotKnowMultiSelect_StillGivesTheEntity()
    {
        var (provider, _) = Create(multiSelectSupported: false);

        var entity = await provider.GetEntityAsync("new_arac");

        Assert.NotNull(entity);
        Assert.Equal(2, entity!.FindAttribute("new_garantili")!.Options.Count);
        Assert.Empty(entity.FindAttribute("new_ozellikler")!.Options);
    }

    /// <summary>
    /// Verifies that the definition request asks for the attribute type name.
    /// </summary>
    [Fact]
    public async Task TheDefinitionRequest_AsksForTheAttributeTypeName()
    {
        var (provider, handler) = Create();

        await provider.GetEntityAsync("new_arac");

        Assert.Contains(handler.Urls, u => Uri.UnescapeDataString(u).Contains("AttributeTypeName"));
    }

    /// <summary>
    /// Deletes the temporary metadata cache directory created for the test, if it exists.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
