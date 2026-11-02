using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Observability.Services;

namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Provides the ask pipeline's knowledge source, searching nothing, not even the embedding model, when the flag is off.
/// </summary>
public sealed class KnowledgeRetriever : IKnowledgeSource
{
    private readonly KnowledgeService _service;
    private readonly Func<bool> _enabled;
    private readonly ILogger _logger;

    public KnowledgeRetriever(KnowledgeService service, bool enabled, ILogger logger)
        : this(service, () => enabled, logger)
    {
    }

    public KnowledgeRetriever(KnowledgeService service, Func<bool> enabled, ILogger logger)
    {
        _service = service;
        _enabled = enabled;
        _logger = logger;
    }

    /// <summary>
    /// Finds company knowledge pieces relevant to a question.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindAsync(string question, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            return new List<string>();
        }
        try
        {
            var snippets = await _service.SearchAsync(question, ct);
            _logger.LogInformation("Request {RequestId}: {Count} knowledge pieces added", RequestContext.Current, snippets.Count);
            RequestContext.RecordKnowledgeSearch(snippets.Count);
            return snippets.Select(s => s.Text).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Request {RequestId}: knowledge search failed, continuing without company knowledge ({ExceptionType})",
                RequestContext.Current, ex.GetType().Name);
            return new List<string>();
        }
    }
}
