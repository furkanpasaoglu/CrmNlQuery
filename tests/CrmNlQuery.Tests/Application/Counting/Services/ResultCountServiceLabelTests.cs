using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Counting.Abstractions;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Tests.Application.Counting.Services;

/// <summary>
/// Tests that the count carries the root table label so text can say accounts instead of records.
/// </summary>
public class ResultCountServiceLabelTests
{
    private sealed class OneCounter : IRecordCounter
    {
        /// <summary>
        /// Returns a fake aggregate count for the test.
        /// </summary>
        public Task<long> AggregateCountAsync(string entitySetName, string aggregateFetchXml, Caller caller, CancellationToken ct = default) => Task.FromResult(8L);
        /// <summary>
        /// Returns a fake total record count for the test.
        /// </summary>
        public Task<RecordCount> TotalCountAsync(string entitySetName, string totalCountFetchXml, Caller caller, CancellationToken ct = default) => throw new CrmException("unused");
    }

    /// <summary>
    /// Verifies that the count carries the root table's label so the text can say accounts instead of records.
    /// </summary>
    [Fact]
    public async Task TheCountCarriesTheRootTablesLabel_SoTheTextCanSayAccountsInsteadOfRecords()
    {
        var history = new MemoryHistoryStore();
        var owner = Guid.NewGuid();
        var id = await history.AddAsync(owner, "q", "account", "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>");
        var metadata = TestMetadata.Create();
        var service = new ResultCountService(history, new CountQueryBuilder(new PlanValidator(metadata), metadata), new OneCounter(), () => TimeSpan.FromSeconds(15));

        var result = await service.CountAsync(owner, id);

        Assert.Equal(CountStatus.Ok, result.Status);
        Assert.Equal("Account", result.EntityLabel);
    }
}
