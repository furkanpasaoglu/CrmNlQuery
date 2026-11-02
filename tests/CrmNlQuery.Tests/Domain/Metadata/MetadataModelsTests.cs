using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Domain.Metadata;

/// <summary>
/// Tests the metadata model helpers such as attribute lookup.
/// </summary>
public class MetadataModelsTests
{
    private static EntityInfo Product() => new EntityInfo(
        "product", "products", "Product", "productid", "name",
        new List<AttributeInfo>
        {
            new AttributeInfo("productid", "Product", "Uniqueidentifier", new List<OptionInfo>()),
            new AttributeInfo("name", "Name", "String", new List<OptionInfo>()),
        },
        new List<RelationshipInfo>());

    /// <summary>
    /// Verifies that find attribute ignores case.
    /// </summary>
    [Fact]
    public void FindAttribute_IgnoresCase()
    {
        Assert.Equal("name", Product().FindAttribute("NAME")?.LogicalName);
    }

    /// <summary>
    /// Verifies that find attribute returns null when missing.
    /// </summary>
    [Fact]
    public void FindAttribute_ReturnsNullWhenMissing()
    {
        Assert.Null(Product().FindAttribute("nope"));
    }
}
