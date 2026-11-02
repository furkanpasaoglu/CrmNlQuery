using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Application.Setup.Services;
using CrmNlQuery.Web.Contracts.Setup;

namespace CrmNlQuery.Web.Endpoints.Setup;

/// <summary>
/// Maps the first-run setup wizard endpoints; they exist only while the service runs in setup mode and answer with short error codes, never with details from the systems that were tested.
/// </summary>
public static class SetupEndpoints
{
    public const string SessionHeader = "X-Setup-Session";

    /// <summary>
    /// Registers the state, unlock, test and complete endpoints; the service stops itself after a successful completion so the container restarts with the saved settings.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app, SetupService setup, IHostApplicationLifetime lifetime)
    {
        app.MapGet("/api/setup/state", async (CancellationToken ct) =>
        {
            var status = await setup.StatusAsync(ct);
            return Results.Json(new SetupStateDto(status.NeedsSetup, status.Locked, status.DatabaseAvailable));
        });

        app.MapPost("/api/setup/unlock", (UnlockDto dto) =>
        {
            var result = setup.Unlock(dto.Code);
            return result.Ok ? Results.Json(new { session = result.Value }) : Fail(result.Error!, null);
        });

        app.MapPost("/api/setup/test-crm", async (CrmProbeInput dto, HttpContext http, CancellationToken ct) =>
        {
            var result = await setup.TestCrmAsync(Session(http), dto, ct);
            return result.Ok ? Results.Json(new { checks = result.Value }) : Fail(result.Error!, result.Field);
        });

        app.MapPost("/api/setup/test-model", async (TestModelDto dto, HttpContext http, CancellationToken ct) =>
        {
            var result = await setup.TestModelAsync(Session(http), dto.Model, dto.ConfirmLocalNetwork, dto.ConfirmCloudLab, ct);
            return result.Ok ? Results.Json(new { checks = result.Value }) : Fail(result.Error!, result.Field);
        });

        app.MapPost("/api/setup/complete", async (SetupInput dto, HttpContext http, CancellationToken ct) =>
        {
            var result = await setup.CompleteAsync(Session(http), dto, ct);
            if (!result.Ok)
            {
                return Fail(result.Error!, result.Field);
            }
            http.Response.OnCompleted(() =>
            {
                _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => lifetime.StopApplication());
                return Task.CompletedTask;
            });
            return Results.Json(new { ok = true });
        });
    }

    private static string? Session(HttpContext http) => http.Request.Headers[SessionHeader].FirstOrDefault();

    private static IResult Fail(string error, string? field)
    {
        var status = error switch
        {
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "locked" => StatusCodes.Status429TooManyRequests,
            "invalid_code" => StatusCodes.Status403Forbidden,
            "not_available" => StatusCodes.Status404NotFound,
            "database_unavailable" => StatusCodes.Status503ServiceUnavailable,
            "save_failed" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        return Results.Json(new { error, field }, statusCode: status);
    }
}
