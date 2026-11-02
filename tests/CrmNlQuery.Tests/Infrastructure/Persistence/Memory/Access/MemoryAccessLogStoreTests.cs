using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Observability.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Runs the access log store contract against the in-memory store.
/// </summary>
public class MemoryAccessLogStoreTests : AccessLogStoreContract
{
    protected override IAccessLogStore CreateStore() => new MemoryAccessLogStore();
}
