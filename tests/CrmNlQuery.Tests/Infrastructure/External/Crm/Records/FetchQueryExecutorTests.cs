using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Tests the FetchXML executor: escaping, impersonation header, neutral result table and length limits.
/// </summary>
public class FetchQueryExecutorTests
{
    private static (FetchQueryExecutor Executor, FakeHandler Handler) Create(string responseJson)
    {
        var handler = new FakeHandler(_ => FakeHandler.Json(responseJson));
        var client = new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" }, allowEvaluationCaller: true);
        return (new FetchQueryExecutor(client), handler);
    }

    /// <summary>
    /// Verifies that run sends escaped fetch XML and returns value array.
    /// </summary>
    [Fact]
    public async Task Run_SendsEscapedFetchXml_AndReturnsValueArray()
    {
        var (executor, handler) = Create("""{"value":[{"name":"Chair"}]}""");
        var fetch = "<fetch top=\"5\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";

        var rows = await executor.RunAsync("products", fetch, Caller.ForEvaluation());

        Assert.Equal(1, rows.GetArrayLength());
        var url = handler.Urls[0];
        Assert.StartsWith("http://crm/test/api/data/v9.0/products?fetchXml=", url);
        Assert.Contains(Uri.EscapeDataString(fetch), url);
    }

    /// <summary>
    /// Verifies that execute with caller sends the impersonation header.
    /// </summary>
    [Fact]
    public async Task Execute_WithCaller_SendsTheImpersonationHeader()
    {
        string? header = null;
        var handler = new FakeHandler(r =>
        {
            header = r.Headers.TryGetValues("MSCRMCallerID", out var values) ? values.Single() : null;
            return FakeHandler.Json("""{"value":[]}""");
        });
        var executor = new FetchQueryExecutor(new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" }));
        var caller = Guid.NewGuid();

        await executor.RunAsync("products", "<fetch />", Caller.User(caller));

        Assert.Equal(caller.ToString(), header);
    }

    /// <summary>
    /// Verifies that execute result outlives response.
    /// </summary>
    [Fact]
    public async Task Execute_ResultOutlivesResponse()
    {
        var (executor, _) = Create("""{"value":[{"name":"Chair"}]}""");
        var rows = await executor.RunAsync("products", "<fetch />", Caller.ForEvaluation());
        Assert.Equal("Chair", rows[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// Verifies that execute returns a CRM neutral table not the web API shape.
    /// </summary>
    [Fact]
    public async Task Execute_ReturnsACrmNeutralTable_NotTheWebApiShape()
    {
        var (executor, _) = Create("""{"value":[{"name":"Chair"},{"name":"Table"}]}""");
        var fetch = "<fetch top=\"5\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>";

        var table = await executor.ExecuteAsync("products", fetch, Caller.ForEvaluation());

        Assert.Equal(new[] { "name" }, table.Columns);
        Assert.Equal(new[] { "Chair", "Table" }, table.Rows.Select(r => r[0]));
    }

    /// <summary>
    /// Verifies that execute too long query throws before calling CRM.
    /// </summary>
    [Fact]
    public async Task Execute_TooLongQuery_ThrowsBeforeCallingCrm()
    {
        var (executor, handler) = Create("""{"value":[]}""");
        var huge = "<fetch>" + new string('x', 5000) + "</fetch>";
        await Assert.ThrowsAsync<CrmException>(() => executor.ExecuteAsync("products", huge, Caller.ForEvaluation()));
        Assert.Empty(handler.Urls);
    }

    /// <summary>
    /// Verifies that evaluation caller is refused when the client does not allow it and nothing is sent.
    /// </summary>
    [Fact]
    public async Task EvaluationCaller_IsRefused_WhenTheClientDoesNotAllowIt_AndNothingIsSent()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"value":[]}"""));
        var executor = new FetchQueryExecutor(new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync("products", "<fetch />", Caller.ForEvaluation()));
        Assert.Empty(handler.Urls);
    }
}
