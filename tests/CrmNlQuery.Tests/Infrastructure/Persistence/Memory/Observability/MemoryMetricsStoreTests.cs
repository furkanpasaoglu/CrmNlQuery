using CrmNlQuery.Tests.Contracts.Observability;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Observability;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Observability;

/// <summary>
/// Runs the metrics store contract tests against the in-memory store.
/// </summary>
public class MemoryMetricsStoreTests : MetricsStoreContract
{
    protected override IMetricsStore CreateStore() => new MemoryMetricsStore();
}
