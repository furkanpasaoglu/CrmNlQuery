using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// Runs the report catalog store contract tests against the in-memory store.
/// </summary>
public class MemoryReportCatalogStoreTests : ReportCatalogStoreContract
{
    protected override IReportCatalogStore CreateStore() => new MemoryReportCatalogStore();
}
