using CrmNlQuery.Tests.Contracts;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory;

/// <summary>
/// Tests the in-memory user view store.
/// </summary>
public class MemoryUserViewStoreTests : UserViewStoreContract
{
    protected override IUserViewStore CreateStore() => new MemoryUserViewStore();

    private static MemoryUserViewStore Memory(IUserViewStore store) => (MemoryUserViewStore)store;

    protected override Task<IReadOnlyList<string>> IdsOfAsync(IUserViewStore store, Guid owner, string name)
    {
        IReadOnlyList<string> ids = Memory(store).All.Where(v => v.Owner == owner && v.Name == name).Select(v => v.Id.ToString("D")).ToList();
        return Task.FromResult(ids);
    }

    protected override Task<string> AddForeignSharedViewAsync(IUserViewStore store, Guid foreignOwner, Guid shareWith, string name)
        => Task.FromResult(Memory(store).AddForeignView(foreignOwner, name, Accounts()).ToString("D"));

    protected override Task<IReadOnlyList<string>> ForeignIdsAsync(IUserViewStore store, Guid foreignOwner, string name)
        => IdsOfAsync(store, foreignOwner, name);

    protected override Task<string> AddDuplicateAsync(IUserViewStore store, Guid owner, string name)
        => Task.FromResult(Memory(store).AddForeignView(owner, name, Accounts(), shared: false).ToString("D"));
}
