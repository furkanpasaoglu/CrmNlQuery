using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Provides canned entity metadata objects used across tests.
/// </summary>
public static class TestMetadata
{
    /// <summary>
    /// Creates the full set of canned test entities.
    /// </summary>
    public static FakeMetadataSource Create() => new FakeMetadataSource(Product(), Account(), Contract(), ContractDetail());

    private static AttributeInfo A(string name, string type, params (int Value, string Label)[] options)
    {
        var list = options.Select(o => new OptionInfo(o.Value, o.Label)).ToList();
        return new AttributeInfo(name, name, type, list);
    }

    /// <summary>
    /// Builds a canned product entity with id, name, product number and state attributes for tests.
    /// </summary>
    public static EntityInfo Product() => new EntityInfo("product", "products", "Product", "productid", "name",
        new List<AttributeInfo>
        {
            A("productid", "Uniqueidentifier"), A("name", "String"), A("productnumber", "String"),
            A("statecode", "State", (0, "Active"), (1, "Retired")),
        },
        new List<RelationshipInfo>());

    /// <summary>
    /// Builds a canned account entity with id, name, city and state attributes for tests.
    /// </summary>
    public static EntityInfo Account() => new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo>
        {
            A("accountid", "Uniqueidentifier"), A("name", "String"), A("address1_city", "String"),
            A("statecode", "State", (0, "Active"), (1, "Inactive")),
        },
        new List<RelationshipInfo>());

    /// <summary>
    /// Builds a canned contract entity with id, title, customer lookup and state attributes for tests.
    /// </summary>
    public static EntityInfo Contract() => new EntityInfo("contract", "contracts", "Contract", "contractid", "title",
        new List<AttributeInfo>
        {
            A("contractid", "Uniqueidentifier"), A("title", "String"), A("customerid", "Customer"),
            A("statecode", "State", (0, "Draft"), (2, "Active")),
        },
        new List<RelationshipInfo>());

    /// <summary>
    /// Builds a canned contract detail entity.
    /// </summary>
    public static EntityInfo ContractDetail() => new EntityInfo("contractdetail", "contractdetails", "Contract Line", "contractdetailid", "title",
        new List<AttributeInfo>
        {
            A("contractdetailid", "Uniqueidentifier"), A("title", "String"), A("contractid", "Lookup"),
            A("productid", "Lookup"), A("price", "Money"),
        },
        new List<RelationshipInfo>
        {
            new RelationshipInfo("contract_contract_details", "contractdetail", "contractid", "contract", "contractid"),
        });
}
