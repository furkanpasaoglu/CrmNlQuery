namespace CrmNlQuery.Application.Knowledge.Abstractions;

/// <summary>
/// Tells whether the knowledge base is usable and why not, so the service can start without pgvector.
/// </summary>
public interface IKnowledgeAvailability
{
    bool IsAvailable { get; }

    string? Reason { get; }
}
