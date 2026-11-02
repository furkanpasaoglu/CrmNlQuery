using CrmNlQuery.Tests.Contracts.Access;
using CrmNlQuery.Application.Preferences.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Access;

/// <summary>
/// Tests the in-memory preferences store against the shared contract.
/// </summary>
public class MemoryPreferencesStoreTests : PreferencesStoreContract
{
    protected override IPreferencesStore CreateStore() => new MemoryPreferencesStore();
}
