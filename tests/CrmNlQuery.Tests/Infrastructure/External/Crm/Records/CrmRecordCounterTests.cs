using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm.Records;

/// <summary>
/// Unit tests for reading CRM count responses with a fake server: aggregate results, returntotalrecordcount hints and error shapes.
/// </summary>
public class CrmRecordCounterTests
{
    private static CrmRecordCounter Counter(FakeHandler handler) =>
        new CrmRecordCounter(new CrmClient(new HttpClient(handler), new CrmOptions { BaseUrl = "http://crm/test" }, allowEvaluationCaller: true));

    /// <summary>
    /// Verifies that the aggregate count is read from the n column as the caller with the query escaped in the URL.
    /// </summary>
    [Fact]
    public async Task TheAggregateCount_IsReadFromTheNColumn_AsTheCaller_WithTheQueryEscapedInTheUrl()
    {
        string? url = null, header = null;
        var handler = new FakeHandler(r =>
        {
            url = r.RequestUri!.AbsoluteUri;
            header = r.Headers.TryGetValues("MSCRMCallerID", out var h) ? h.Single() : null;
            return FakeHandler.Json("{\"value\":[{\"n\":6020}]}");
        });
        var caller = Guid.NewGuid();

        var count = await Counter(handler).AggregateCountAsync("accounts", "<fetch aggregate=\"true\"><entity name=\"account\" /></fetch>", Caller.User(caller));

        Assert.Equal(6020, count);
        Assert.Equal(caller.ToString(), header);
        Assert.StartsWith("http://crm/test/api/data/v9.0/accounts?fetchXml=", url);
        Assert.DoesNotContain("<fetch", url);
    }

    /// <summary>
    /// Verifies that a malformed aggregate answer is a CRM exception so the fallback takes over.
    /// </summary>
    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("{\"value\":[{\"other\":1}]}")]
    [InlineData("{\"value\":[{\"n\":\"abc\"}]}")]
    public async Task AMalformedAggregateAnswer_IsACrmException_SoTheFallbackTakesOver(string body)
    {
        var counter = Counter(new FakeHandler(_ => FakeHandler.Json(body)));

        await Assert.ThrowsAsync<CrmException>(() => counter.AggregateCountAsync("accounts", "<fetch />", Caller.ForEvaluation()));
    }

    /// <summary>
    /// Verifies that a CRM error on the aggregate becomes a CRM exception.
    /// </summary>
    [Fact]
    public async Task ACrmError_OnTheAggregate_BecomesACrmException()
    {
        var counter = Counter(new FakeHandler(_ => FakeHandler.Json("{\"error\":{\"message\":\"AggregateQueryRecordLimit exceeded. Cannot perform this operation.\"}}", System.Net.HttpStatusCode.BadRequest)));

        var ex = await Assert.ThrowsAsync<CrmException>(() => counter.AggregateCountAsync("accounts", "<fetch />", Caller.ForEvaluation()));

        Assert.Equal(400, ex.StatusCode);
    }

    /// <summary>
    /// Verifies that the total record count and its limit flag are read from the annotations.
    /// </summary>
    [Fact]
    public async Task TheTotalRecordCount_AndItsLimitFlag_AreReadFromTheAnnotations()
    {
        var exact = Counter(new FakeHandler(_ => FakeHandler.Json("{\"value\":[{}],\"@Microsoft.Dynamics.CRM.totalrecordcount\":8,\"@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded\":false}")));
        var capped = Counter(new FakeHandler(_ => FakeHandler.Json("{\"value\":[{}],\"@Microsoft.Dynamics.CRM.totalrecordcount\":5000,\"@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded\":true}")));

        var a = await exact.TotalCountAsync("accounts", "<fetch />", Caller.ForEvaluation());
        var b = await capped.TotalCountAsync("accounts", "<fetch />", Caller.ForEvaluation());

        Assert.Equal(8, a.Total);
        Assert.False(a.LimitExceeded);
        Assert.Equal(5000, b.Total);
        Assert.True(b.LimitExceeded);
    }

    /// <summary>
    /// Verifies that a total count answer without a usable number is a CRM exception.
    /// </summary>
    [Theory]
    [InlineData("{\"value\":[{}]}")]
    [InlineData("{\"value\":[{}],\"@Microsoft.Dynamics.CRM.totalrecordcount\":-1}")]
    [InlineData("{\"value\":[{}],\"@Microsoft.Dynamics.CRM.totalrecordcount\":\"x\"}")]
    public async Task ATotalCountAnswerWithoutAUsableNumber_IsACrmException(string body)
    {
        var counter = Counter(new FakeHandler(_ => FakeHandler.Json(body)));

        await Assert.ThrowsAsync<CrmException>(() => counter.TotalCountAsync("accounts", "<fetch />", Caller.ForEvaluation()));
    }

    /// <summary>
    /// Verifies that a missing limit flag means not exceeded.
    /// </summary>
    [Fact]
    public async Task AMissingLimitFlag_MeansNotExceeded()
    {
        var counter = Counter(new FakeHandler(_ => FakeHandler.Json("{\"value\":[{}],\"@Microsoft.Dynamics.CRM.totalrecordcount\":3}")));

        Assert.False((await counter.TotalCountAsync("accounts", "<fetch />", Caller.ForEvaluation())).LimitExceeded);
    }

    /// <summary>
    /// Verifies that an oversized query is refused before sending.
    /// </summary>
    [Fact]
    public async Task AnOversizedQuery_IsRefusedBeforeSending()
    {
        var sent = false;
        var counter = Counter(new FakeHandler(_ => { sent = true; return FakeHandler.Json("{}"); }));

        await Assert.ThrowsAsync<CrmException>(() => counter.AggregateCountAsync("accounts", new string('x', 5000), Caller.ForEvaluation()));

        Assert.False(sent);
    }
}
