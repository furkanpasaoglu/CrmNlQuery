using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Counting.Models;
using CrmNlQuery.Application.CrmViews.Models;
using CrmNlQuery.Application.DataPolicy.Exceptions;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Web.Contracts.Ask;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// Maps the ask endpoints: asking, opening results and narrowing; each first checks identity and does nothing when it cannot be determined.
/// </summary>
public static class AskEndpoints
{
    /// <summary>
    /// Builds the refusal response for a gate denial: 429 for a user limit or 503 for pause or overall load, with Retry-After.
    /// </summary>
    internal static IResult Refused(HttpContext http, GateDenial denial)
    {
        http.Response.Headers.RetryAfter = denial.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(OutcomeDto.From(new FailureOutcome(denial.Message, denial.Kind)), statusCode: denial.HttpStatus);
    }

    /// <summary>
    /// Maps the ask, result and narrowing endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapPost("/api/ask", async (AskDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();

            if (services.Create is { } create)
            {
                var wanted = Guid.TryParse(dto.CreateDraftId, out var pendingId) ? pendingId : (Guid?)null;
                if (await create.IsCandidateAsync(dto.Question ?? "", wanted, callerId!, ct))
                {
                    var canCreate = await WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Constants.Permissions.RecordsCreate, ct);
                    var attempt = await services.Guard.RunAsync(callerId!, token => create.HandleAsync(dto.Question ?? "", wanted, callerId!, canCreate, WidgetPermissions.Account(http, services) ?? "", token), ct);
                    if (attempt.Denial is { } blocked)
                    {
                        return Refused(http, blocked);
                    }
                    if (attempt.Outcome is { } handled)
                    {
                        if (handled is FailureOutcome { Kind: "timeout" })
                        {
                            return Results.Json(OutcomeDto.From(handled), statusCode: StatusCodes.Status504GatewayTimeout);
                        }
                        return Results.Json(OutcomeDto.From(handled));
                    }
                }
            }

            var answers = dto.Answers ?? new Dictionary<string, string>();
            var turn = await services.ChatTurns.PrepareAsync(callerId!.UserId, dto.ChatId, ct);
            var previous = turn.Previous;
            var currentRecord = dto.Context is null ? null : await services.RecordContext.ResolveAsync(dto.Context.Entity, dto.Context.Id, callerId, ct);
            if (answers.Count == 0 && await services.Reports.TryFindAsync(dto.Question ?? "", callerId, currentRecord, ct) is { } reportCard)
            {
                var storedReport = await services.ChatTurns.CompleteReportAsync(reportCard, callerId.UserId, dto.Question ?? "", turn, ct);
                await CountReportsShownAsync(services, reportCard);
                return Results.Json(OutcomeDto.From(storedReport));
            }
            var guarded = await services.Guard.RunAsync(callerId,
                async token => await services.Ask.RunAsync(new AskRequest(dto.Question ?? "", answers, callerId, previous?.Query, CurrentRecord: currentRecord), token), ct);
            if (guarded.Denial is { } denial)
            {
                return Refused(http, denial);
            }
            var outcome = guarded.Outcome!;
            if (outcome is FailureOutcome { Kind: "timeout" })
            {
                return Results.Json(OutcomeDto.From(outcome), statusCode: StatusCodes.Status504GatewayTimeout);
            }
            if (outcome is AnswerOutcome answer)
            {
                outcome = await services.ChatTurns.CompleteAsync(answer, callerId.UserId, dto.Question ?? "", turn, ct);
            }
            else if (outcome is SummaryOutcome summary)
            {
                outcome = await services.ChatTurns.CompleteSummaryAsync(summary, callerId.UserId, dto.Question ?? "", turn, ct);
            }
            else if (outcome is DraftOutcome draft)
            {
                outcome = await services.ChatTurns.CompleteDraftAsync(draft, callerId.UserId, dto.Question ?? "", turn, ct);
            }
            return Results.Json(OutcomeDto.From(await WithCrmOffer(services, http, outcome, ct)));
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/refine", async (string id, RefineDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var guarded = await services.Guard.RunAsync(callerId!, token => services.Refine.RefineAsync(id, callerId!.UserId, dto.Text, dto.Answers, token), ct);
            return await GuardedResultAsync(services, http, guarded, rejectFailures: false, ct);
        }).RequireCaller();

        app.MapGet("/api/result/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();

            return await StoredQueryAsync(async () =>
            {
                AskOutcome? result;
                if (await services.Results.IsSummaryAsync(id, callerId!.UserId, ct))
                {
                    var guarded = await services.Guard.RunAsync(callerId, token => services.Results.OpenAnyAsync(id, callerId.UserId, token), ct);
                    if (guarded.Denial is { } denial)
                    {
                        return Refused(http, denial);
                    }
                    result = guarded.Outcome;
                }
                else
                {
                    result = await services.Results.OpenAnyAsync(id, callerId.UserId, ct);
                }
                if (result is null)
                {
                    return Results.Json(OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.ResultGone))), statusCode: 404);
                }
                return Results.Json(OutcomeDto.From(await WithCrmOffer(services, http, result, ct, countShown: false)));
            });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/explain/edit", async (string id, ExplainEditDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            return await StoredQueryAsync(async () =>
            {
                var edit = new ExplainEdit(dto.ChipId ?? "", dto.Action ?? "", dto.Value);
                var guarded = await services.Guard.RunAsync(callerId!, token => services.ExplainEdit.EditAsync(id, callerId!.UserId, edit, token), ct);
                return await GuardedResultAsync(services, http, guarded, rejectFailures: true, ct);
            });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/suggest/apply", async (string id, SuggestionApplyDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            return await StoredQueryAsync(async () =>
            {
                var guarded = await services.Guard.RunAsync(callerId!, token => services.Suggestions.ApplyAsync(id, callerId!.UserId, dto.SuggestionId ?? "", token), ct);
                return await GuardedResultAsync(services, http, guarded, rejectFailures: true, ct);
            });
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/suggestions/click", async (SuggestionClickDto dto, CancellationToken ct) =>
        {
            await services.Suggestions.ClickAsync(dto.Kind ?? "", ct);
            return Results.NoContent();
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/reports/click", async (ReportClickDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            try
            {
                var card = await services.Results.OpenAnyAsync(dto.ResultId ?? "", callerId!.UserId, ct) as ReportOutcome;
                var item = card?.Reports.FirstOrDefault(r => string.Equals(r.Id, dto.ReportId, StringComparison.OrdinalIgnoreCase));
                if (item is not null) { await services.ReportStats.AddOpenedAsync(item.Name, ct); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
            return Results.NoContent();
        }).RequireCors("crm-widget").RequireCaller();

        app.MapGet("/api/result/{id}/count", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            var result = await services.ResultCounts.CountAsync(callerId!.UserId, id, ct);
            return result.Status switch
            {
                CountStatus.Ok => Results.Json(new { total = result.Total, limitExceeded = result.LimitExceeded, entityLabel = result.EntityLabel }),
                CountStatus.NotFound => Results.NotFound(),
                CountStatus.NotEligible => Results.StatusCode(StatusCodes.Status422UnprocessableEntity),
                _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            };
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/open-in-crm", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            if (!await WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Constants.Permissions.RecordsOpenInCrm, ct))
            {
                return Results.NotFound();
            }
            var result = await services.OpenInCrm.OpenAsync(callerId!.UserId, id, ct);
            return result.Status switch
            {
                OpenInCrmStatus.Opened => Results.Json(new { url = result.Url }),
                OpenInCrmStatus.Disabled or OpenInCrmStatus.NotFound => Results.NotFound(),
                OpenInCrmStatus.NotEligible => Results.Json(new { message = MessageCatalog.Now(Msg.OpenInCrmNotEligibleResult) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                OpenInCrmStatus.PermissionDenied => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status502BadGateway),
            };
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/result/{id}/section/open-in-crm", async (string id, SectionOpenDto dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            if (!await WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Constants.Permissions.RecordsOpenInCrm, ct))
            {
                return Results.NotFound();
            }
            var result = await services.OpenInCrm.OpenSectionAsync(callerId!.UserId, id, dto.Kind ?? "", dto.Entity ?? "", dto.ViaEntity, ct);
            return result.Status switch
            {
                OpenInCrmStatus.Opened => Results.Json(new { url = result.Url }),
                OpenInCrmStatus.Disabled or OpenInCrmStatus.NotFound => Results.NotFound(),
                OpenInCrmStatus.NotEligible => Results.Json(new { message = MessageCatalog.Now(Msg.OpenInCrmNotEligibleSection) }, statusCode: StatusCodes.Status422UnprocessableEntity),
                OpenInCrmStatus.PermissionDenied => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status502BadGateway),
            };
        }).RequireCors("crm-widget").RequireCaller();
    }

    private static async Task<IResult> GuardedResultAsync(AppServices services, HttpContext http, GuardedResult guarded, bool rejectFailures, CancellationToken ct)
    {
        if (guarded.Denial is { } denial)
        {
            return Refused(http, denial);
        }
        var outcome = guarded.Outcome;
        if (outcome is FailureOutcome { Kind: "timeout" })
        {
            return Results.Json(OutcomeDto.From(outcome), statusCode: StatusCodes.Status504GatewayTimeout);
        }
        if (outcome is null)
        {
            return Results.Json(OutcomeDto.From(new FailureOutcome(MessageCatalog.Now(Msg.ResultGone))), statusCode: 404);
        }
        if (rejectFailures && outcome is FailureOutcome rejected)
        {
            return Results.Json(OutcomeDto.From(rejected), statusCode: 422);
        }
        return Results.Json(OutcomeDto.From(await WithCrmOffer(services, http, outcome, ct)));
    }

    private static async Task<IResult> StoredQueryAsync(Func<Task<IResult>> run)
    {
        try
        {
            return await run();
        }
        catch (DataPolicyException ex)
        {
            return Results.Json(OutcomeDto.From(new FailureOutcome(ex.Message, "data_policy")), statusCode: 403);
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return Results.Json(OutcomeDto.From(new FailureOutcome(ex.Message)), statusCode: 403);
        }
        catch (CrmException ex) when (ex.IsImpersonationDenied)
        {
            return Results.Json(OutcomeDto.From(new FailureOutcome(ex.Message)), statusCode: 503);
        }
        catch (CrmException ex)
        {
            return Results.Json(OutcomeDto.From(new FailureOutcome(UserMessages.ForCrm(ex))), statusCode: 503);
        }
    }

    private static async Task CountReportsShownAsync(AppServices services, ReportOutcome card)
    {
        try
        {
            await services.ReportStats.AddShownAsync(card.Reports.Select(r => r.Name).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    private static async Task<AskOutcome> WithCrmOffer(AppServices services, HttpContext http, AskOutcome outcome, CancellationToken ct, bool countShown = true)
    {
        var mayOpenInCrm = await WidgetPermissions.HasAsync(http, services, CrmNlQuery.Domain.Access.Constants.Permissions.RecordsOpenInCrm, ct);
        if (outcome is SummaryOutcome summary)
        {
            var recordId = Guid.TryParse(summary.RecordId, out var parsed) ? parsed : Guid.Empty;
            var sections = mayOpenInCrm ? await services.OpenInCrm.OfferSectionsAsync(summary.Entity, recordId, summary.Sections, ct) : summary.Sections;
            return summary with { Sections = sections, RecordUrlBase = $"{services.CrmBaseUrl.TrimEnd('/')}/main.aspx?pagetype=entityrecord&etn=" };
        }
        if (outcome is CrmNlQuery.Application.Create.Models.CreateOutcome created)
        {
            return created with { RecordUrlBase = $"{services.CrmBaseUrl.TrimEnd('/')}/main.aspx?pagetype=entityrecord&etn=" };
        }
        if (outcome is not AnswerOutcome answer)
        {
            return outcome;
        }
        var recordUrlBase = $"{services.CrmBaseUrl.TrimEnd('/')}/main.aspx?etn={Uri.EscapeDataString(answer.Entity)}&pagetype=entityrecord&id=";
        var explanation = await services.Explainer.ExplainAsync(answer.FetchXml, answer.ResolvedRecords, ct);
        var chart = answer.FromChain ? null : CrmNlQuery.Application.Charts.Services.ChartBuilder.Build(answer.FetchXml, answer.Table, answer.Truncated);
        var chartReason = chart is not null ? null : answer.FromChain ? "chain" : CrmNlQuery.Application.Charts.Services.ChartBuilder.WhyNot(answer.FetchXml, answer.Table);
        var columnLabels = await ColumnLabeler.TryLabelAsync(answer.FetchXml, answer.Table.Columns, services.Metadata, ct);
        var suggestions = await services.Suggestions.ForAsync(answer, countShown, ct);
        return answer with { Suggestions = suggestions, OpenInCrm = mayOpenInCrm && await services.OpenInCrm.CanOfferAsync(answer, ct), RecordUrlBase = recordUrlBase, Explanation = explanation, ShowFetchXml = services.ShowFetchXml(), Chart = chart, ChartReason = chartReason, ColumnLabels = columnLabels };
    }
}
