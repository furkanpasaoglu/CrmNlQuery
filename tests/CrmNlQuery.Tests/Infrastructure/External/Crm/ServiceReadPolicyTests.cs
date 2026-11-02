using CrmNlQuery.Tests.Support;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Tests that service-account reads are limited to an allowlist of system endpoints and never business data.
/// </summary>
public class ServiceReadPolicyTests
{
    /// <summary>
    /// Verifies that the real system reads are allowed.
    /// </summary>
    [Theory]
    [InlineData("WhoAmI")]
    [InlineData("EntityDefinitions?$select=LogicalName,EntitySetName,DisplayName,IsPrivate")]
    [InlineData("EntityDefinitions(LogicalName='account')?$select=ObjectTypeCode")]
    [InlineData("EntityDefinitions(LogicalName='account')?$select=LogicalName,EntitySetName&$expand=Attributes($select=LogicalName,AttributeType),ManyToOneRelationships($select=ReferencingAttribute)")]
    [InlineData("EntityDefinitions(LogicalName='account')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options)")]
    [InlineData("savedqueries?$select=layoutxml&$top=1&$filter=returnedtypecode%20eq%20%27account%27%20and%20isdefault%20eq%20true%20and%20querytype%20eq%200")]
    [InlineData("systemusers?$select=systemuserid&$filter=domainname%20eq%20%27CONTOSO%5Ctestuser_a%27%20and%20isdisabled%20eq%20false&$top=2")]
    [InlineData("systemusers?$select=systemuserid,fullname&$filter=systemuserid%20eq%20bdab3908-c0c7-f111-b4de-00155d01f303")]
    public void TheRealSystemReads_AreAllowed(string url)
    {
        ServiceReadPolicy.EnsureAllowed(url);
    }

    /// <summary>
    /// Verifies that the sign-in proof task can be read only by its exact subject, with the creator fields only.
    /// </summary>
    [Fact]
    public void TheSignInProofTask_IsReadableOnlyBySubjectWithCreatorFields()
    {
        var nonce = new string('a', 43);
        ServiceReadPolicy.EnsureAllowed("tasks?$select=activityid,createdon,modifiedon,_createdby_value,_modifiedby_value,_createdonbehalfby_value,_modifiedonbehalfby_value&$filter=" + Uri.EscapeDataString("subject eq 'NLQ-login-" + nonce + "'") + "&$top=2");
    }

    /// <summary>
    /// Verifies that every other read of tasks is refused.
    /// </summary>
    [Theory]
    [InlineData("tasks")]
    [InlineData("tasks?$select=activityid&$top=2")]
    [InlineData("tasks?$select=subject,description&$filter=subject%20eq%20%27NLQ-login-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa%27&$top=2")]
    [InlineData("tasks?$select=activityid&$filter=startswith(subject,%27NLQ-login-%27)&$top=2")]
    [InlineData("tasks?$select=activityid&$filter=subject%20eq%20%27NLQ-login-short%27&$top=2")]
    [InlineData("tasks?$select=activityid&$filter=subject%20eq%20%27NLQ-login-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa%27")]
    [InlineData("tasks?$select=activityid&$filter=subject%20eq%20%27NLQ-login-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa%27&$top=50")]
    [InlineData("tasks?$select=activityid&$filter=subject%20eq%20%27NLQ-login-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa%27&$top=2&$orderby=createdon")]
    [InlineData("tasks(11111111-1111-1111-1111-111111111111)?$select=activityid")]
    [InlineData("activitypointers?$select=activityid&$top=2")]
    public void AnyOtherTaskRead_IsRefused(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ServiceReadPolicy.EnsureAllowed(url));
    }

    /// <summary>
    /// Verifies that business data and anything else is refused.
    /// </summary>
    [Theory]
    [InlineData("accounts?$select=name")]
    [InlineData("contacts")]
    [InlineData("opportunities?$top=5")]
    [InlineData("products?$select=name&$top=1")]
    [InlineData("incidents(00000000-0000-0000-0000-000000000001)")]
    [InlineData("accounts?fetchXml=%3Cfetch%3E%3Centity%20name%3D%22account%22%2F%3E%3C%2Ffetch%3E")]
    [InlineData("systemusers?fetchXml=x")]
    [InlineData("%61ccounts?$select=name")]
    [InlineData("http://crm.example.com/contoso/api/data/v9.0/accounts")]
    [InlineData("/accounts")]
    [InlineData("EntityDefinitions/../accounts")]
    [InlineData("EntityDefinitions(LogicalName='account')/../../accounts")]
    [InlineData("$batch")]
    [InlineData("RetrieveMultiple")]
    [InlineData("WhoAmI/x")]
    [InlineData("")]
    [InlineData("   ")]
    public void BusinessDataAndAnythingElse_IsRefused(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ServiceReadPolicy.EnsureAllowed(url));
    }

    /// <summary>
    /// Verifies that system user and view reads are narrow only the needed columns no expand.
    /// </summary>
    [Theory]
    [InlineData("systemusers?$select=address1_telephone1")]
    [InlineData("systemusers?$select=systemuserid,internalemailaddress")]
    [InlineData("systemusers")]
    [InlineData("systemusers?$select=systemuserid&$expand=systemuserroles_association")]
    [InlineData("savedqueries?$select=layoutxml&$expand=owninguser")]
    [InlineData("savedqueries")]
    [InlineData("savedqueries?$select=layoutxml&$top=5000")]
    public void SystemUserAndViewReads_AreNarrow_OnlyTheNeededColumns_NoExpand(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ServiceReadPolicy.EnsureAllowed(url));
    }

    /// <summary>
    /// Verifies that metadata reads accept only known shapes.
    /// </summary>
    [Theory]
    [InlineData("EntityDefinitions?$select=LogicalName&fetchXml=x")]
    [InlineData("EntityDefinitions(LogicalName='account')?$select=LogicalName&$apply=groupby((x))")]
    [InlineData("EntityDefinitions(LogicalName='a;b')?$select=LogicalName")]
    [InlineData("EntityDefinitions(LogicalName='account')/Attributes?$select=LogicalName")]
    public void MetadataReads_AcceptOnlyKnownShapes(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ServiceReadPolicy.EnsureAllowed(url));
    }

    /// <summary>
    /// Verifies that the client refuses before anything is sent.
    /// </summary>
    [Fact]
    public async Task TheClient_RefusesBeforeAnythingIsSent()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{\"value\":[]}"));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsServiceAsync("accounts?$select=name"));

        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that the client still sends an allowed read.
    /// </summary>
    [Fact]
    public async Task TheClient_StillSendsAnAllowedRead()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{\"value\":[]}"));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });

        using var doc = await client.GetAsServiceAsync("systemusers?$select=systemuserid,fullname&$filter=systemuserid%20eq%20bdab3908-c0c7-f111-b4de-00155d01f303");

        Assert.Single(handler.Urls);
    }

    /// <summary>
    /// Verifies that the user path is not restricted by this policy it runs as the user with the users rights.
    /// </summary>
    [Fact]
    public async Task TheUserPath_IsNotRestrictedByThisPolicy_ItRunsAsTheUserWithTheUsersRights()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("{\"value\":[]}"));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" });

        using var doc = await client.GetAsync("accounts?$select=name", CrmNlQuery.Domain.Access.Entities.Caller.User(Guid.NewGuid()));

        Assert.Single(handler.Urls);
    }
}
