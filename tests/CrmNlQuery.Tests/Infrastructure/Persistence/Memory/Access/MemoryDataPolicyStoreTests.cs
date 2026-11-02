using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Runs the data policy store contract tests against the in-memory store.
/// </summary>
public class MemoryDataPolicyStoreTests : DataPolicyStoreContract
{
    protected override IDataPolicyStore CreateStore() => new MemoryDataPolicyStore();
}
