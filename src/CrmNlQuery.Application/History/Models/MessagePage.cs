using CrmNlQuery.Domain.History.Entities;

namespace CrmNlQuery.Application.History.Models;

/// <summary>
/// Holds a page of a chat's messages ordered oldest to newest with a cursor for older ones.
/// </summary>
public sealed record MessagePage(IReadOnlyList<HistoryEntry> Items, string? NextCursor);
