using CrmNlQuery.Composition;
using CrmNlQuery.Web.Security;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Web.Endpoints.Ask;

/// <summary>
/// Maps the record-creation confirm and cancel endpoints; writing happens only here and only after the user's explicit click.
/// </summary>
public static class CreateEndpoints
{
    /// <summary>
    /// Maps the create endpoints onto the route builder.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, AppServices services)
    {
        app.MapPost("/api/create/{id}/confirm", async (string id, CreateConfirmDto? dto, HttpContext http, CancellationToken ct) =>
        {
            var callerId = http.Caller();
            if (services.Create is not { } create || !Guid.TryParse(id, out var draftId))
            {
                return Results.NotFound();
            }
            var canCreate = await WidgetPermissions.HasAsync(http, services, Permissions.RecordsCreate, ct);
            var account = WidgetPermissions.Account(http, services) ?? "";
            var guarded = await services.Guard.RunAsync(callerId!, async token => await create.ConfirmAsync(draftId, callerId!, account, dto?.AllowDuplicates == true, canCreate, token), ct);
            if (guarded.Denial is { } denial)
            {
                return AskEndpoints.Refused(http, denial);
            }
            var outcome = guarded.Outcome!;
            if (outcome is FailureOutcome { Kind: "timeout" })
            {
                return Results.Json(OutcomeDto.From(outcome), statusCode: StatusCodes.Status504GatewayTimeout);
            }
            if (outcome is CreateOutcome { Kind: "done" } done)
            {
                var turn = await services.ChatTurns.PrepareAsync(callerId!.UserId, dto?.ChatId, ct);
                done = await services.ChatTurns.CompleteCreateAsync(done, callerId.UserId, done.Question ?? "", turn, ct);
                outcome = done with { RecordUrlBase = $"{services.CrmBaseUrl.TrimEnd('/')}/main.aspx?pagetype=entityrecord&etn=" };
            }
            return Results.Json(OutcomeDto.From(outcome));
        }).RequireCors("crm-widget").RequireCaller();

        app.MapPost("/api/create/{id}/cancel", (string id, HttpContext http) =>
        {
            var callerId = http.Caller();
            if (services.Create is not { } create || !Guid.TryParse(id, out var draftId))
            {
                return Results.NotFound();
            }
            return Results.Json(OutcomeDto.From(create.Cancel(draftId, callerId!)));
        }).RequireCors("crm-widget").RequireCaller();
    }
}
