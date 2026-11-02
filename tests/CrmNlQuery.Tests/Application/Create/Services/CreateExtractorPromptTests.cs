using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Tests the record extractor prompt: which fields are hidden or shown and how common fields are ordered.
/// </summary>
public class CreateExtractorPromptTests
{
    private sealed class CapturingLlm : ILlmClient
    {
        public string User { get; private set; } = "";
        /// <summary>
        /// Test fake model client that returns a canned JSON completion.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            User = user;
            return Task.FromResult("{\"entity\":\"account\",\"records\":[{\"values\":[]}]}");
        }
    }

    private static CreateField Text(string name, string label) =>
        new CreateField(name, label, CreateFieldType.Text, false, 50, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>());

    private static CreateSchema Schema(params CreateField[] fields) =>
        new CreateSchema("account", "accounts", "Account", "accountid", "name", fields);

    /// <summary>
    /// Verifies that the prompt hides duplicate address phone fields and keeps the main phone.
    /// </summary>
    [Fact]
    public async Task Prompt_HidesDuplicateAddressPhones_AndKeepsMainPhone()
    {
        var llm = new CapturingLlm();
        var schema = Schema(Text("name", "Account Name"), Text("address1_telephone1", "Address 1: Telephone 1"), Text("telephone1", "Main Phone"), Text("address1_fax", "Address 1: Fax"));

        await new CreateExtractor(llm).ExtractAsync("x", new[] { schema }, null);

        Assert.Contains("telephone1 (Main Phone)", llm.User);
        Assert.DoesNotContain("address1_telephone1", llm.User);
        Assert.DoesNotContain("address1_fax", llm.User);
    }

    private static CreateSchema ContactSchema() =>
        new CreateSchema("contact", "contacts", "Contact", "contactid", "fullname", new[] { Text("lastname", "Last Name"), Text("company", "Company Name"), Text("jobtitle", "Job Title") });

    private static async Task<string> PromptFor(CreateSchema schema, params string[] hidden)
    {
        var llm = new CapturingLlm();
        await new CreateExtractor(llm, null, () => hidden).ExtractAsync("x", new[] { schema }, null);
        return llm.User;
    }

    /// <summary>
    /// Verifies that the built-in hidden list hides the contact company text field.
    /// </summary>
    [Fact]
    public async Task TheBuiltInList_HidesTheContactCompanyTextField()
    {
        var prompt = await PromptFor(ContactSchema());

        Assert.DoesNotContain("company (", prompt);
        Assert.Contains("jobtitle (Job Title)", prompt);
    }

    /// <summary>
    /// Verifies that the admin-configured list hides more fields for that environment only.
    /// </summary>
    [Fact]
    public async Task TheAdminList_HidesMoreFields_ForThatEnvironmentOnly()
    {
        var prompt = await PromptFor(ContactSchema(), "contact.jobtitle");

        Assert.DoesNotContain("jobtitle (", prompt);
    }

    /// <summary>
    /// Verifies that a bang entry shows a field the built in list hides.
    /// </summary>
    [Fact]
    public async Task ABangEntry_ShowsAFieldTheBuiltInListHides()
    {
        var prompt = await PromptFor(ContactSchema(), "!contact.company");

        Assert.Contains("company (Company Name)", prompt);
    }

    /// <summary>
    /// Verifies that the same field name on another table is not hidden.
    /// </summary>
    [Fact]
    public async Task TheSameFieldNameOnAnotherTable_IsNotHidden()
    {
        var account = Schema(Text("name", "Account Name"), Text("company", "Company"));

        Assert.Contains("company (Company)", await PromptFor(account));
    }

    /// <summary>
    /// Verifies that the prompt keeps the common fields even when many other fields precede them in the schema.
    /// </summary>
    [Fact]
    public async Task Prompt_KeepsTheCommonFields_EvenWhenManyOtherFieldsPrecedeThemInTheSchema()
    {
        var llm = new CapturingLlm();
        var fields = new List<CreateField> { Text("name", "Account Name") };
        for (var i = 0; i < 90; i++)
        {
            fields.Add(Text("custom_field" + i, "Custom " + i));
        }
        fields.Add(Text("telephone1", "Main Phone"));
        fields.Add(Text("address1_city", "City"));

        await new CreateExtractor(llm).ExtractAsync("x", new[] { Schema(fields.ToArray()) }, null);

        Assert.Contains("telephone1 (Main Phone)", llm.User);
        Assert.Contains("address1_city (City)", llm.User);
    }

    /// <summary>
    /// Verifies that the prompt lists common fields before secondary ones when the list is cut.
    /// </summary>
    [Fact]
    public async Task Prompt_ListsCommonFieldsBeforeSecondaryOnes_WhenTheListIsCut()
    {
        var llm = new CapturingLlm();
        var fields = new List<CreateField> { Text("name", "Account Name") };
        for (var i = 0; i < 70; i++)
        {
            fields.Add(Text("address2_field" + i, "Address 2 " + i));
        }
        fields.Add(Text("telephone1", "Main Phone"));

        await new CreateExtractor(llm).ExtractAsync("x", new[] { Schema(fields.ToArray()) }, null);

        Assert.Contains("telephone1 (Main Phone)", llm.User);
    }
}
