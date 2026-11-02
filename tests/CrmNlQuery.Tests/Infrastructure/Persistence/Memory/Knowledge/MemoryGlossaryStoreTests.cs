using CrmNlQuery.Tests.Contracts.Knowledge;
using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Knowledge;

/// <summary>
/// Runs the shared glossary store contract tests against the in-memory glossary store.
/// </summary>
public class MemoryGlossaryStoreTests : GlossaryStoreContract
{
    protected override IGlossaryStore CreateStore() => new MemoryGlossaryStore();
}
