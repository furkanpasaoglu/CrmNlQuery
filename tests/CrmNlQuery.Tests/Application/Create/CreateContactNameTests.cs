using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Tests.Application.Create;

/// <summary>
/// Tests that a contact's display name is built from first and last name because fullname is read-only in CRM.
/// </summary>
public class CreateContactNameTests
{
    private static CreateField Text(string name, string label, bool required = false) =>
        new CreateField(name, label, CreateFieldType.Text, required, 50, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>());

    private static CreateSchema ContactSchema() => new CreateSchema("contact", "contacts", "Contact", "contactid", "fullname", new[]
    {
        Text("firstname", "First Name"),
        Text("lastname", "Last Name", required: true),
        Text("emailaddress1", "Email"),
    });

    private static CreateValidator Validator()
        => new CreateValidator((entity, text, ct) => Task.FromResult<IReadOnlyList<RecordMatch>>(Array.Empty<RecordMatch>()));

    /// <summary>
    /// Verifies that the display name is built from first and last name.
    /// </summary>
    [Fact]
    public async Task TheDisplayName_IsBuiltFromFirstAndLastName()
    {
        var row = await Validator().ValidateAsync(ContactSchema(),
            new Dictionary<string, string> { ["firstname"] = "Ayşe", ["lastname"] = "Yılmaz" });

        Assert.True(row.IsComplete);
        Assert.Equal("Ayşe Yılmaz", row.DisplayName);
    }

    /// <summary>
    /// Verifies that with only a last name the display name is the last name and the row is complete.
    /// </summary>
    [Fact]
    public async Task WithOnlyALastName_TheDisplayNameIsTheLastName_AndTheRowIsComplete()
    {
        var row = await Validator().ValidateAsync(ContactSchema(), new Dictionary<string, string> { ["lastname"] = "Demir" });

        Assert.True(row.IsComplete);
        Assert.Equal("Demir", row.DisplayName);
    }

    /// <summary>
    /// Verifies that without a last name the row asks for it.
    /// </summary>
    [Fact]
    public async Task WithoutALastName_TheRowAsksForIt()
    {
        var row = await Validator().ValidateAsync(ContactSchema(), new Dictionary<string, string> { ["firstname"] = "Ayşe" });

        Assert.False(row.IsComplete);
        Assert.Contains(row.Problems, p => p.Code == "missing_required" && p.Attribute == "lastname");
    }
}
