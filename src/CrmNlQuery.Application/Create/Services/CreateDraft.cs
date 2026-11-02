namespace CrmNlQuery.Application.Create.Services;

/// <summary>
/// Holds a pending record-creation draft containing only the raw texts the user gave, row by row; validation is redone on every use.
/// </summary>
public sealed class CreateDraft
{
    public CreateDraft(Guid id, Guid owner, string entity, string question, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, DateTimeOffset createdAt)
    {
        Id = id;
        Owner = owner;
        Entity = entity;
        Question = question;
        Rows = rows;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid Owner { get; }

    public string Entity { get; }

    public string Question { get; }

    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Returns a copy of the draft with the given rows.
    /// </summary>
    public CreateDraft WithRows(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) => new CreateDraft(Id, Owner, Entity, Question, rows, CreatedAt);
}
