namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// Export filters: dataset, format, date range and optional account, kind, user and table filters.
/// </summary>
public sealed record ExportRequest(string Dataset, ExportFormat Format, DateTimeOffset From, DateTimeOffset To)
{
    public string? Account { get; init; }
    public Guid? UserId { get; init; }
    public string? Table { get; init; }
    public string? Kind { get; init; }
    public string? TimeZone { get; init; }
}
