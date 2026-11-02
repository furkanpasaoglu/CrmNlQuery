using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// Runs the shared knowledge store tests against the memory store.
/// </summary>
public class MemoryKnowledgeStoreTests : KnowledgeStoreContract
{
    private readonly MemoryKnowledgeStore _store = new MemoryKnowledgeStore();

    protected override IKnowledgeStore CreateStore() => _store;
}
