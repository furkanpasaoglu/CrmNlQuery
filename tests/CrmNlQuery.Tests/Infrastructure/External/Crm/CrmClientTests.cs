using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Tests the CRM HTTP client: error mapping, impersonation headers, permission messages and write requests.
/// </summary>
public class CrmClientTests
{
    private static CrmClient Client(FakeHandler handler, bool allowEvaluationCaller = false) =>
        new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" }, allowEvaluationCaller);

    /// <summary>
    /// Verifies that non JSON body becomes CRM exception.
    /// </summary>
    [Fact]
    public async Task NonJsonBody_BecomesCrmException()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("<html>Please sign in</html>")));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.Contains("JSON", ex.Message);
    }

    /// <summary>
    /// Verifies that timeout becomes CRM exception.
    /// </summary>
    [Fact]
    public async Task Timeout_BecomesCrmException()
    {
        var client = Client(new FakeHandler(_ => throw new TaskCanceledException()));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.Contains("in time", ex.Message);
    }

    /// <summary>
    /// Verifies that HTTP error becomes CRM exception with status code.
    /// </summary>
    [Fact]
    public async Task HttpError_BecomesCrmException_WithStatusCode()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("""{"error":{"message":"boom"}}""", System.Net.HttpStatusCode.InternalServerError)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.Equal(500, ex.StatusCode);
    }

    /// <summary>
    /// Verifies that a user caller is sent as ms CRM caller ID header.
    /// </summary>
    [Fact]
    public async Task AUserCaller_IsSentAsMsCrmCallerIdHeader()
    {
        string? header = null;
        var client = Client(new FakeHandler(r =>
        {
            header = r.Headers.TryGetValues("MSCRMCallerID", out var values) ? values.Single() : null;
            return FakeHandler.Json("{}");
        }));
        var user = Guid.NewGuid();
        await client.GetAsync("accounts", Caller.User(user));
        Assert.Equal(user.ToString(), header);
    }

    /// <summary>
    /// Verifies that a system read is named and sends no impersonation header.
    /// </summary>
    [Fact]
    public async Task ASystemRead_IsNamed_AndSendsNoImpersonationHeader()
    {
        var sawHeader = true;
        var client = Client(new FakeHandler(r =>
        {
            sawHeader = r.Headers.Contains("MSCRMCallerID");
            return FakeHandler.Json("{}");
        }));
        await client.GetAsServiceAsync("WhoAmI");
        Assert.False(sawHeader);
    }

    /// <summary>
    /// Verifies that the evaluation caller is refused when the client is not in evaluation mode and nothing is sent.
    /// </summary>
    [Fact]
    public async Task TheEvaluationCaller_IsRefused_WhenTheClientIsNotInEvaluationMode_AndNothingIsSent()
    {
        var sent = 0;
        var client = Client(new FakeHandler(_ => { sent++; return FakeHandler.Json("{}"); }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("accounts", Caller.ForEvaluation()));

        Assert.Equal(0, sent);
    }

    /// <summary>
    /// Verifies that the evaluation caller is accepted in evaluation mode without an impersonation header.
    /// </summary>
    [Fact]
    public async Task TheEvaluationCaller_IsAccepted_InEvaluationMode_WithoutAnImpersonationHeader()
    {
        var sawHeader = true;
        var client = Client(new FakeHandler(r =>
        {
            sawHeader = r.Headers.Contains("MSCRMCallerID");
            return FakeHandler.Json("{}");
        }), allowEvaluationCaller: true);

        await client.GetAsync("accounts", Caller.ForEvaluation());

        Assert.False(sawHeader);
    }

    private const string MissingPrivilegeBody =
        """{"error":{"code":"0x80040220","message":"Principal user (Id=b688bb0e-a4c8-f111-b4e0-00155d01f303, type=8) is missing prvReadContract privilege (Id=8022a167-ea97-4c91-a41c-b77c083f7113)"}}""";

    /// <summary>
    /// Verifies that missing privilege becomes friendly permission exception without GUIDs.
    /// </summary>
    [Fact]
    public async Task MissingPrivilege_BecomesFriendlyPermissionException_WithoutGuids()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json(MissingPrivilegeBody, System.Net.HttpStatusCode.Forbidden)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.True(ex.IsPermissionDenied);
        Assert.Equal("You do not have permission to read Contract records.", ex.Message);
        Assert.DoesNotContain("b688bb0e", ex.Message);
    }

    /// <summary>
    /// Verifies that missing privilege is detected whatever the HTTP status.
    /// </summary>
    [Fact]
    public async Task MissingPrivilege_IsDetectedWhateverTheHttpStatus()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json(MissingPrivilegeBody, System.Net.HttpStatusCode.InternalServerError)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.True(ex.IsPermissionDenied);
    }

    /// <summary>
    /// Verifies that permission error with unreadable privilege name gets a generic message.
    /// </summary>
    [Fact]
    public async Task PermissionError_WithUnreadablePrivilegeName_GetsAGenericMessage()
    {
        var body = """{"error":{"code":"0x80040220","message":"something unexpected"}}""";
        var client = Client(new FakeHandler(_ => FakeHandler.Json(body, System.Net.HttpStatusCode.Forbidden)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.True(ex.IsPermissionDenied);
        Assert.Equal("You do not have permission to read this kind of record.", ex.Message);
    }

    /// <summary>
    /// Verifies that the aggregate record limit is flagged.
    /// </summary>
    [Theory]
    [InlineData("""{"error":{"code":"0x8004E023","message":"The maximum record limit is exceeded. Reduce the number of records."}}""")]
    [InlineData("""{"error":{"message":"AggregateQueryRecordLimit exceeded. Cannot perform this operation."}}""")]
    [InlineData("""{"error":{"message":"The maximum record limit is exceeded. Reduce the number of records."}}""")]
    public async Task TheAggregateRecordLimit_IsFlagged(string body)
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json(body, System.Net.HttpStatusCode.BadRequest)));

        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));

        Assert.True(ex.IsAggregateLimit);
        Assert.False(ex.IsPermissionDenied);
    }

    /// <summary>
    /// Verifies that an ordinary bad request is not the aggregate limit.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryBadRequest_IsNotTheAggregateLimit()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("""{"error":{"code":"0x80040203","message":"bad value"}}""", System.Net.HttpStatusCode.BadRequest)));

        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));

        Assert.False(ex.IsAggregateLimit);
    }

    /// <summary>
    /// Verifies that other forbidden is not a permission denial.
    /// </summary>
    [Fact]
    public async Task OtherForbidden_IsNotAPermissionDenial()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("""{"error":{"message":"nope"}}""", System.Net.HttpStatusCode.Forbidden)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.False(ex.IsPermissionDenied);
    }

    private const string ActOnBehalfBody =
        """{"error":{"code":"0x80040204","message":"User does not have the privilege to act on behalf another user.","innererror":{"message":"x"}}}""";

    /// <summary>
    /// Verifies that act on behalf denied becomes an actionable setup message.
    /// </summary>
    [Fact]
    public async Task ActOnBehalfDenied_BecomesAnActionableSetupMessage()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json(ActOnBehalfBody, System.Net.HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsync("accounts", Caller.User(Guid.NewGuid())));
        Assert.True(ex.IsImpersonationDenied);
        Assert.False(ex.IsPermissionDenied);
        Assert.Contains("Act on Behalf of Another User", ex.Message);
        Assert.DoesNotContain("0x8004", ex.Message);
    }

    /// <summary>
    /// Verifies that other unauthorized is not mistaken for an impersonation problem.
    /// </summary>
    [Fact]
    public async Task OtherUnauthorized_IsNotMistakenForAnImpersonationProblem()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("""{"error":{"message":"HTTP Error 401.1 - Unauthorized"}}""", System.Net.HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<CrmException>(() => client.GetAsServiceAsync("WhoAmI"));
        Assert.False(ex.IsImpersonationDenied);
    }

    /// <summary>
    /// Verifies that integrated auth uses the process identity no password needed.
    /// </summary>
    [Fact]
    public void IntegratedAuth_UsesTheProcessIdentity_NoPasswordNeeded()
    {
        var handler = CrmClient.CreateHandler(new CrmOptions { UseIntegratedAuth = true, Username = "ignored", Password = "ignored" });
        Assert.True(handler.UseDefaultCredentials);
    }

    /// <summary>
    /// Verifies that configured credentials are used when integrated auth is off.
    /// </summary>
    [Fact]
    public void ConfiguredCredentials_AreUsedWhenIntegratedAuthIsOff()
    {
        var handler = CrmClient.CreateHandler(new CrmOptions { Username = "svc", Password = "p", Domain = "CONTOSO" });
        Assert.False(handler.UseDefaultCredentials);
        Assert.NotNull(handler.Credentials);
    }

    /// <summary>
    /// Verifies that caller cancellation is not wrapped.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_IsNotWrapped()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("{}")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsServiceAsync("WhoAmI", cts.Token));
    }

    /// <summary>
    /// Verifies that a write is sent as the caller with the right method URL and body.
    /// </summary>
    [Fact]
    public async Task AWrite_IsSentAsTheCaller_WithTheRightMethodUrlAndBody()
    {
        string? method = null, header = null, prefer = null, url = null;
        var handler = new FakeHandler(r =>
        {
            method = r.Method.Method;
            url = r.RequestUri!.AbsoluteUri;
            header = r.Headers.TryGetValues("MSCRMCallerID", out var h) ? h.Single() : null;
            prefer = r.Headers.TryGetValues("Prefer", out var p) ? p.Single() : null;
            return FakeHandler.Json("{\"userqueryid\":\"abc\"}", System.Net.HttpStatusCode.Created);
        });
        var caller = Guid.NewGuid();

        using var created = await Client(handler).SendJsonAsync(HttpMethod.Post, "userqueries", "{\"name\":\"x\"}", caller);

        Assert.Equal("POST", method);
        Assert.EndsWith("/api/data/v9.0/userqueries", url);
        Assert.Equal(caller.ToString(), header);
        Assert.Equal("return=representation", prefer);
        Assert.Equal("{\"name\":\"x\"}", handler.Bodies.Single());
        Assert.Equal("abc", created!.RootElement.GetProperty("userqueryid").GetString());
    }

    /// <summary>
    /// Verifies that a bodyless success returns null for patch and delete.
    /// </summary>
    [Fact]
    public async Task ABodylessSuccess_ReturnsNull_ForPatchAndDelete()
    {
        var client = Client(new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)));

        Assert.Null(await client.SendJsonAsync(HttpMethod.Patch, "userqueries(1)", "{}", Guid.NewGuid()));
        Assert.Null(await client.SendJsonAsync(HttpMethod.Delete, "userqueries(1)", null, Guid.NewGuid()));
    }

    /// <summary>
    /// Verifies that a write can not be a get.
    /// </summary>
    [Fact]
    public async Task AWriteCanNotBeAGet()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("{}")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendJsonAsync(HttpMethod.Get, "accounts", null, Guid.NewGuid()));
    }

    /// <summary>
    /// Verifies that a missing privilege on a write or read becomes a plain permission error.
    /// </summary>
    [Theory]
    [InlineData("Principal user (Id=1, type=8) is missing prvCreateUserQuery privilege (Id=2)", "change")]
    [InlineData("Principal user (Id=1, type=8) is missing prvWriteUserQuery privilege (Id=2)", "change")]
    [InlineData("Principal user (Id=1, type=8) is missing prvDeleteUserQuery privilege (Id=2)", "change")]
    [InlineData("Principal user (Id=1, type=8) is missing prvReadAccount privilege (Id=2)", "read Account")]
    public async Task AMissingPrivilege_OnAWriteOrRead_BecomesAPlainPermissionError(string crmMessage, string expectedPart)
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("{\"error\":{\"message\":\"" + crmMessage + "\"}}", System.Net.HttpStatusCode.Forbidden)));

        var ex = await Assert.ThrowsAsync<CrmException>(() => client.SendJsonAsync(HttpMethod.Post, "userqueries", "{}", Guid.NewGuid()));

        Assert.True(ex.IsPermissionDenied);
        Assert.Contains(expectedPart, ex.Message);
        Assert.DoesNotContain("Principal", ex.Message);
        Assert.DoesNotContain("prv", ex.Message);
    }

    /// <summary>
    /// Verifies that act on behalf denied on a write is the same admin facing error.
    /// </summary>
    [Fact]
    public async Task ActOnBehalfDenied_OnAWrite_IsTheSameAdminFacingError()
    {
        var client = Client(new FakeHandler(_ => FakeHandler.Json("{\"error\":{\"message\":\"0x80040204 act on behalf\"}}", System.Net.HttpStatusCode.Forbidden)));

        var ex = await Assert.ThrowsAsync<CrmException>(() => client.SendJsonAsync(HttpMethod.Post, "userqueries", "{}", Guid.NewGuid()));

        Assert.True(ex.IsImpersonationDenied);
    }
}
