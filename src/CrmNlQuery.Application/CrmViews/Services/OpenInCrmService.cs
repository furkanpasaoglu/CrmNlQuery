using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.CrmViews.Abstractions;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.CrmViews.Services;

/// <summary>
/// Opens large results in the CRM's own view by writing the user's personal view, as the caller.
/// </summary>
public sealed class OpenInCrmService
{
    public const string ViewName = "NL Query - last result";

    private readonly IHistoryStore _history;
    private readonly ViewBuilder _builder;
    private readonly IUserViewStore _views;
    private readonly Func<bool> _enabled;
    private readonly string _crmBaseUrl;
    private readonly CrmNlQuery.Application.Summary.Services.SummaryBuilder? _summary;

    public OpenInCrmService(IHistoryStore history, ViewBuilder builder, IUserViewStore views, Func<bool> enabled, string crmBaseUrl, CrmNlQuery.Application.Summary.Services.SummaryBuilder? summary = null)
    {
        _summary = summary;
        _history = history;
        _builder = builder;
        _views = views;
        _enabled = enabled;
        _crmBaseUrl = crmBaseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Tells whether an answer should show Open in CRM: flag on, result truncated, stored and convertible to a view.
    /// </summary>
    public async Task<bool> CanOfferAsync(AnswerOutcome answer, CancellationToken ct = default)
    {
        if (!_enabled() || !answer.Truncated || answer.ResultId is null || answer.FromChain)
        {
            return false;
        }
        try
        {
            return (await _builder.BuildAsync(answer.FetchXml, ct)).Ok;
        }
        catch (CrmException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates or updates the caller's personal CRM view for a stored result and returns its address.
    /// </summary>
    public async Task<OpenInCrmResult> OpenAsync(Guid caller, string resultId, CancellationToken ct = default)
    {
        if (!_enabled())
        {
            return new OpenInCrmResult(OpenInCrmStatus.Disabled);
        }
        var entry = await _history.GetAsync(caller, resultId, ct);
        if (entry is null || entry.Kind is not null)
        {
            return new OpenInCrmResult(OpenInCrmStatus.NotFound);
        }

        return await OpenFetchAsync(caller, entry.FetchXml, ct);
    }

    /// <summary>
    /// Opens one section of a customer summary in CRM, rebuilding its list query from the stored summary root record for the owner only.
    /// </summary>
    public async Task<OpenInCrmResult> OpenSectionAsync(Guid caller, string resultId, string kind, string entity, string? viaEntity, CancellationToken ct = default)
    {
        if (!_enabled() || _summary is null)
        {
            return new OpenInCrmResult(OpenInCrmStatus.Disabled);
        }
        var fetch = await SectionFetchAsync(caller, resultId, kind, entity, viaEntity, ct);
        return fetch is null ? new OpenInCrmResult(OpenInCrmStatus.NotFound) : await OpenFetchAsync(caller, fetch, ct);
    }

    /// <summary>
    /// Checks in one discovery pass whether Open in CRM can be offered for every summary section, never failing the summary.
    /// </summary>
    public async Task<IReadOnlyList<CrmNlQuery.Application.Summary.Models.SummarySection>> OfferSectionsAsync(string rootEntity, Guid rootId, IReadOnlyList<CrmNlQuery.Application.Summary.Models.SummarySection> sections, CancellationToken ct = default)
    {
        if (!_enabled() || _summary is null)
        {
            return sections;
        }
        IReadOnlyDictionary<string, string> fetches;
        try
        {
            fetches = await _summary.ListFetchesAsync(rootEntity, rootId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return sections;
        }
        var result = new List<CrmNlQuery.Application.Summary.Models.SummarySection>();
        foreach (var section in sections)
        {
            var offer = false;
            if (section.Kind != CrmNlQuery.Application.Summary.Models.SummaryKinds.Basic && section.Status == CrmNlQuery.Application.Summary.Models.SummaryStatus.Ok
                && fetches.TryGetValue(CrmNlQuery.Application.Summary.Services.SummaryBuilder.SectionKey(section.Kind, section.Entity, section.ViaEntity), out var fetch))
            {
                try
                {
                    offer = (await _builder.BuildAsync(fetch, ct)).Ok;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    offer = false;
                }
            }
            result.Add(section with { CanOpen = offer });
        }
        return result;
    }

    /// <summary>
    /// Tells whether Open in CRM should show for a summary section: flag on and the list query convertible to a view.
    /// </summary>
    public async Task<bool> CanOfferSectionAsync(string rootEntity, Guid rootId, CrmNlQuery.Application.Summary.Models.SummarySection section, CancellationToken ct = default)
    {
        if (!_enabled() || _summary is null || section.Kind == CrmNlQuery.Application.Summary.Models.SummaryKinds.Basic || section.Status != CrmNlQuery.Application.Summary.Models.SummaryStatus.Ok)
        {
            return false;
        }
        try
        {
            var fetch = await _summary.ListFetchXmlAsync(rootEntity, rootId, section.Kind, section.Entity, section.ViaEntity, ct);
            return fetch is not null && (await _builder.BuildAsync(fetch, ct)).Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<string?> SectionFetchAsync(Guid caller, string resultId, string kind, string entity, string? viaEntity, CancellationToken ct)
    {
        var entry = await _history.GetAsync(caller, resultId, ct);
        var recordId = entry is { Kind: ResultService.SummaryKind } ? CrmNlQuery.Application.Summary.Services.SummaryRecord.IdOf(entry.FetchXml) : null;
        return recordId is null ? null : await _summary!.ListFetchXmlAsync(entry!.Entity, recordId.Value, kind, entity, viaEntity, ct);
    }

    private async Task<OpenInCrmResult> OpenFetchAsync(Guid caller, string fetchXml, CancellationToken ct)
    {
        var built = await _builder.BuildAsync(fetchXml, ct);
        if (!built.Ok)
        {
            return new OpenInCrmResult(OpenInCrmStatus.NotEligible, Message: built.Rejection.ToString());
        }

        try
        {
            var id = await _views.UpsertAsync(caller, ViewName, built.View!, ct);
            return new OpenInCrmResult(OpenInCrmStatus.Opened, Url: ViewUrl(built.View!.Entity, id));
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return new OpenInCrmResult(OpenInCrmStatus.PermissionDenied, Message: MessageCatalog.Now(Msg.OpenInCrmDenied));
        }
        catch (CrmException)
        {
            return new OpenInCrmResult(OpenInCrmStatus.Failed, Message: MessageCatalog.Now(Msg.OpenInCrmFailed));
        }
    }

    /// <summary>
    /// Deletes the caller's temporary view, even when the flag is off, when all history is deleted.
    /// </summary>
    public async Task DeleteViewAsync(Guid caller, CancellationToken ct = default)
    {
        try
        {
            await _views.DeleteAsync(caller, ViewName, ct);
        }
        catch (CrmException)
        {
        }
    }

    private string ViewUrl(string entity, string viewId)
    {
        var id = Guid.TryParse(viewId, out var guid) ? guid.ToString("D") : viewId;
        return $"{_crmBaseUrl}/main.aspx?etn={Uri.EscapeDataString(entity)}&pagetype=entitylist&viewid=%7b{id}%7d&viewtype=4230";
    }
}
