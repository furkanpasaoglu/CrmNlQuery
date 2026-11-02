namespace CrmNlQuery.Infrastructure.Persistence.Postgres;

/// <summary>
/// Records a schema migration that was skipped with its reason.
/// </summary>
public sealed record SkippedMigration(int Version, string Reason);
