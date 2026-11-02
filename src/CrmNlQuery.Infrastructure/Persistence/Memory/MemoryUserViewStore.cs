using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Memory;

/// <summary>
/// In-memory user view store used when no database is configured and in tests.
/// </summary>
public sealed class MemoryUserViewStore : IUserViewStore
{
    /// <summary>
    /// In-memory record of a stored user view: id, owner, name, definition and shared flag.
    /// </summary>
    public sealed record StoredView(Guid Id, Guid Owner, string Name, ViewDefinition Definition, bool Shared);

    private readonly object _gate = new object();
    private readonly List<StoredView> _views = new List<StoredView>();

    public IReadOnlyList<StoredView> All
    {
        get
        {
            lock (_gate)
            {
                return _views.ToList();
            }
        }
    }

    /// <summary>
    /// Test helper that adds a view owned by someone else (optionally shared); the contract says the caller must never touch it.
    /// </summary>
    public Guid AddForeignView(Guid owner, string name, ViewDefinition definition, bool shared = true)
    {
        lock (_gate)
        {
            var id = Guid.NewGuid();
            _views.Add(new StoredView(id, owner, name, definition, shared));
            return id;
        }
    }

    /// <summary>
    /// Creates or updates the caller's named view in memory and returns its id.
    /// </summary>
    public Task<string> UpsertAsync(Guid caller, string name, ViewDefinition view, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var mine = _views.Where(v => v.Owner == caller && v.Name == name).ToList();
            if (mine.Count == 0)
            {
                return Task.FromResult(Create(caller, name, view));
            }

            var first = mine[0];
            foreach (var extra in mine.Skip(1))
            {
                _views.Remove(extra);
            }
            if (!string.Equals(first.Definition.Entity, view.Entity, StringComparison.Ordinal))
            {
                _views.Remove(first);
                return Task.FromResult(Create(caller, name, view));
            }
            _views[_views.IndexOf(first)] = first with { Definition = view };
            return Task.FromResult(first.Id.ToString("D"));
        }
    }

    /// <summary>
    /// Deletes the caller's named view from memory.
    /// </summary>
    public Task DeleteAsync(Guid caller, string name, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _views.RemoveAll(v => v.Owner == caller && v.Name == name);
        }
        return Task.CompletedTask;
    }

    private string Create(Guid caller, string name, ViewDefinition view)
    {
        var id = Guid.NewGuid();
        _views.Add(new StoredView(id, caller, name, view, false));
        return id.ToString("D");
    }
}
