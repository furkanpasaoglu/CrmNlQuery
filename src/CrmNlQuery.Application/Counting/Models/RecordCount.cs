namespace CrmNlQuery.Application.Counting.Models;

/// <summary>
/// Total record count; when the limit is exceeded the total is only what CRM could count.
/// </summary>
public sealed record RecordCount(long Total, bool LimitExceeded);
