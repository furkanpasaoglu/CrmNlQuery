using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Runs the shared settings store tests against the memory store.
/// </summary>
public class MemorySettingsStoreTests : SettingsStoreContract
{
    private readonly MemorySettingsStore _store = new MemorySettingsStore();

    protected override ISettingsStore CreateStore() => _store;
}
