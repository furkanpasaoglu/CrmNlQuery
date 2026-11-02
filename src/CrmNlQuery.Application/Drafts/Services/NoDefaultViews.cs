using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Application.Drafts.Services;

/// <summary>
/// Default-view source that returns no columns, so draft values skip the basic section.
/// </summary>
public sealed class NoDefaultViews : IDefaultViewSource
{
    /// <summary>
    /// Returns an empty default-column list, so draft value reads never add view columns beyond the ones explicitly requested.
    /// </summary>
    public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
