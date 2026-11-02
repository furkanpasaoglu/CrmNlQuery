using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Setup;
using CrmNlQuery.Tests.Contracts.Setup;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Setup;

public class MemoryAppConfigStoreTests : AppConfigStoreContract
{
    protected override IAppConfigStore CreateStore() => new MemoryAppConfigStore();
}
