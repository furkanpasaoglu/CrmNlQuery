using System.Net;
using System.Text.Json;
using CrmNlQuery.Application.Common.Exceptions;
using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Tries the CRM connection typed in the setup wizard step by step (sign-in, schema, users, acting on behalf of another user, roles and teams, sign-in proof tasks) and reports each result; it never returns business data.
/// </summary>
public sealed class CrmSetupProbe : ICrmProbe
{
    private const string UnusedProof = "subject eq 'NLQ-login-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'";

    /// <summary>
    /// Runs every check; a failed step is recorded and the steps that depend on it are skipped.
    /// </summary>
    public async Task<IReadOnlyList<SetupCheck>> RunAsync(CrmProbeInput input, CancellationToken ct = default)
    {
        var checks = new List<SetupCheck>();
        if (string.Equals(new Uri(input.BaseUrl).Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new SetupCheck("crm.http", SetupCheckStatus.Warn));
        }
        var options = new CrmOptions { BaseUrl = input.BaseUrl.Trim().TrimEnd('/'), Domain = input.Domain.Trim(), Username = input.Username.Trim(), Password = input.Password };
        var handler = CrmClient.CreateHandler(options);
        handler.AllowAutoRedirect = false;
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var client = new CrmClient(http, options);

        Guid? ownId = null;
        if (!await StepAsync(checks, "crm.signin", async () =>
        {
            using var doc = await client.GetAsServiceAsync("WhoAmI", ct);
            ownId = ReadGuid(doc.RootElement, "UserId");
            return ownId is null ? "unexpected_answer" : null;
        }))
        {
            return checks;
        }

        await StepAsync(checks, "crm.schema", async () =>
        {
            using var doc = await client.GetAsServiceAsync("EntityDefinitions(LogicalName='account')", ct);
            return null;
        });

        var others = new List<Guid>();
        await StepAsync(checks, "crm.users", async () =>
        {
            using var doc = await client.GetAsServiceAsync("systemusers?$select=systemuserid,isdisabled&$top=20", ct);
            foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var id = ReadGuid(row, "systemuserid");
                var disabled = row.TryGetProperty("isdisabled", out var d) && d.ValueKind == JsonValueKind.True;
                if (id is not null && id != ownId && !disabled) { others.Add(id.Value); }
            }
            return null;
        });

        if (others.Count == 0)
        {
            checks.Add(new SetupCheck("crm.impersonate", SetupCheckStatus.Warn, "no_other_user"));
        }
        else
        {
            await StepAsync(checks, "crm.impersonate", async () =>
            {
                using var doc = await client.GetAsync("accounts?$select=accountid&$top=1", Caller.User(others[0]), ct);
                return null;
            });
        }

        var ownRoles = new List<string>();
        var groupsStatus = await StepAsync(checks, "crm.groups", async () =>
        {
            using var doc = await client.GetAsServiceAsync($"systemusers({ownId:D})/systemuserroles_association?$select=name&$top=200", ct);
            foreach (var row in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                if (row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) { ownRoles.Add(n.GetString()!); }
            }
            return null;
        }, failStatus: SetupCheckStatus.Warn);

        await StepAsync(checks, "crm.tasks", async () =>
        {
            using var doc = await client.GetAsServiceAsync("tasks?$select=activityid&$filter=" + Uri.EscapeDataString(UnusedProof) + "&$top=1", ct);
            return null;
        });

        if (groupsStatus && ownRoles.Any(r => r.Contains("System Administrator", StringComparison.OrdinalIgnoreCase)))
        {
            checks.Add(new SetupCheck("crm.admin", SetupCheckStatus.Warn));
        }
        return checks;
    }

    private static async Task<bool> StepAsync(List<SetupCheck> checks, string id, Func<Task<string?>> step, SetupCheckStatus failStatus = SetupCheckStatus.Fail)
    {
        try
        {
            var problem = await step();
            checks.Add(problem is null ? new SetupCheck(id, SetupCheckStatus.Ok) : new SetupCheck(id, failStatus, problem));
            return problem is null;
        }
        catch (CrmException ex)
        {
            checks.Add(new SetupCheck(id, failStatus, Short(ex)));
            return false;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or HttpRequestException)
        {
            checks.Add(new SetupCheck(id, failStatus, "unexpected_answer"));
            return false;
        }
    }

    private static string Short(CrmException ex)
    {
        if (ex.StatusCode == (int)HttpStatusCode.Unauthorized) { return "unauthorized"; }
        if (ex.StatusCode == (int)HttpStatusCode.Forbidden) { return "forbidden"; }
        if (ex.StatusCode == (int)HttpStatusCode.NotFound) { return "not_found"; }
        var text = ex.Message ?? "";
        if (text.StartsWith("Cannot reach CRM", StringComparison.Ordinal)) { return "unreachable"; }
        if (text.StartsWith("CRM did not answer", StringComparison.Ordinal)) { return "timeout"; }
        return "error";
    }

    private static Guid? ReadGuid(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;
    }
}
