using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Application.Knowledge.Models;

/// <summary>
/// Raw candidates of one knowledge search: vector hits above the threshold, keyword hits and fused result.
/// </summary>
public sealed record KnowledgeSearchTrace(IReadOnlyList<KnowledgeSnippet> Vector, IReadOnlyList<KnowledgeSnippet> Text, IReadOnlyList<KnowledgeSnippet> Fused);
