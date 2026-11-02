using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Tests the in-memory access store against the shared access store contract.
/// </summary>
public class MemoryAccessStoreTests : AccessStoreContract
{
    protected override IAccessStore CreateStore() => new MemoryAccessStore();
}
