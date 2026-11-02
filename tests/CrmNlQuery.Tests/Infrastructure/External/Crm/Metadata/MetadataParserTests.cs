using System.Text.Json;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Metadata;

/// <summary>
/// Tests parsing of CRM entity metadata.
/// </summary>
public class MetadataParserTests
{
    private const string EntityJson = """
    {"LogicalName":"product","EntitySetName":"products","PrimaryIdAttribute":"productid","PrimaryNameAttribute":"name",
     "DisplayName":{"UserLocalizedLabel":{"Label":"Product"}},
     "Attributes":[
       {"LogicalName":"productid","AttributeType":"Uniqueidentifier","DisplayName":{"UserLocalizedLabel":{"Label":"Product"}}},
       {"LogicalName":"statecode","AttributeType":"State","DisplayName":{"UserLocalizedLabel":{"Label":"Status"}}},
       {"LogicalName":"hidden","AttributeType":"String","DisplayName":{"UserLocalizedLabel":null}}],
     "ManyToOneRelationships":[],
     "OneToManyRelationships":[{"SchemaName":"contract_details_product","ReferencingEntity":"contractdetail","ReferencingAttribute":"productid","ReferencedEntity":"product","ReferencedAttribute":"productid"}]}
    """;

    /// <summary>
    /// Verifies that ParseEntity reads labels, options and relationships.
    /// </summary>
    [Fact]
    public void ParseEntity_ReadsLabelsOptionsAndRelationships()
    {
        var options = new Dictionary<string, IReadOnlyList<OptionInfo>>
        {
            ["statecode"] = new List<OptionInfo> { new OptionInfo(0, "Active"), new OptionInfo(1, "Retired") }
        };
        var entity = MetadataParser.ParseEntity(JsonDocument.Parse(EntityJson).RootElement, options);

        Assert.Equal("products", entity.EntitySetName);
        Assert.Equal("Product", entity.Label);
        Assert.Equal("name", entity.PrimaryNameAttribute);
        Assert.Equal(2, entity.FindAttribute("statecode")!.Options.Count);
        Assert.Equal("hidden", entity.FindAttribute("hidden")!.Label);
        Assert.Single(entity.Relationships);
    }

    /// <summary>
    /// Verifies that the entity list parser skips private entities and entities without a set name.
    /// </summary>
    [Fact]
    public void ParseEntityList_SkipsPrivateEntitiesAndEntitiesWithoutSetName()
    {
        var json = """
        {"value":[
          {"LogicalName":"account","EntitySetName":"accounts","IsPrivate":false,"DisplayName":{"UserLocalizedLabel":{"Label":"Account"}}},
          {"LogicalName":"secret","EntitySetName":"secrets","IsPrivate":true,"DisplayName":{"UserLocalizedLabel":{"Label":"Secret"}}},
          {"LogicalName":"nosetname","EntitySetName":null,"IsPrivate":false,"DisplayName":{"UserLocalizedLabel":null}}]}
        """;
        var list = MetadataParser.ParseEntityList(JsonDocument.Parse(json).RootElement);
        Assert.Single(list);
        Assert.Equal("account", list[0].LogicalName);
    }
}
