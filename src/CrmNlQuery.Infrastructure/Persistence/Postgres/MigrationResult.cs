namespace CrmNlQuery.Infrastructure.Persistence.Postgres;

/// <summary>
/// Result of running schema migrations, listing the skipped ones.
/// </summary>
public sealed record MigrationResult(IReadOnlyList<SkippedMigration> Skipped);
