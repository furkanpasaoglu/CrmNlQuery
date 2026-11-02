using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Decides who may use the tool under the Access:RequireAppUse setting: off, report or on.
/// </summary>
public sealed class AppUseGate
{
    private readonly AccessService _access;
    private readonly Func<string> _mode;
    private readonly ILogger? _logger;

    public AppUseGate(AccessService access, Func<string> mode, ILogger? logger = null)
    {
        _access = access;
        _mode = mode;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether an account may use the app from its permissions and AD group membership.
    /// </summary>
    public async Task<AppUseDecision> EvaluateAsync(string account, Func<string, bool> isMember, string endpoint, Func<string?> describeGroups, CancellationToken ct = default)
    {
        var mode = (_mode() ?? "").Trim().ToLowerInvariant();
        if (mode == "off")
        {
            return new AppUseDecision(AppUseDecisionKind.Allowed);
        }
        var report = mode == "report";

        AccessPrincipal? principal;
        try
        {
            principal = await _access.ResolveAsync(account, ct, isMember);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not check the App.Use permission ({Mode} mode): {Endpoint}", mode, endpoint);
            return new AppUseDecision(report ? AppUseDecisionKind.Allowed : AppUseDecisionKind.Unavailable);
        }

        if (principal is not null && principal.Has(Permissions.AppUse))
        {
            return new AppUseDecision(AppUseDecisionKind.Allowed);
        }

        var reason = principal is null ? AdminAccess.NoAccess : AdminAccess.MissingPermission;
        try
        {
            await _access.RecordAccessAsync(report ? "access.would_deny" : "access.denied", account, endpoint, Permissions.AppUse, reason, describeGroups, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not write the App.Use audit record for {Endpoint}.", endpoint);
        }
        return new AppUseDecision(report ? AppUseDecisionKind.Allowed : AppUseDecisionKind.Denied);
    }
}
