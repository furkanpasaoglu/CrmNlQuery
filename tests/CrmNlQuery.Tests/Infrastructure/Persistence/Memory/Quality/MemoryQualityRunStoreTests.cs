using CrmNlQuery.Tests.Contracts.Quality;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Infrastructure.Persistence.Memory.Quality;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Memory.Quality;

/// <summary>
/// Runs the quality run store contract tests against the in-memory store.
/// </summary>
public class MemoryQualityRunStoreTests : QualityRunStoreContract
{
    protected override IQualityRunStore CreateStore() => new MemoryQualityRunStore();
}
