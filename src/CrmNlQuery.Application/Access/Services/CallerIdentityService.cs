using CrmNlQuery.Application.Access.Abstractions;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Domain.Access.Entities;
namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Determines who is making the request and fails closed: if identity cannot be resolved it refuses instead of falling back to the service account.
/// </summary>
public sealed class CallerIdentityService
{
    private readonly IdentityOptions _options;
    private readonly ICallerResolver _resolver;

    public CallerIdentityService(IdentityOptions options, ICallerResolver resolver)
    {
        _options = options;
        _resolver = resolver;
    }

    /// <summary>
    /// Resolves the CRM caller from the Windows account verified by IIS, or null when no verified user exists.
    /// </summary>
    public async Task<CallerResult> GetCallerAsync(string? windowsAccount = null, CancellationToken ct = default)
    {
        var mode = (_options.Mode ?? "").Trim();
        if (mode.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveAsync(_options.FixedUser, "Identity:Mode is Fixed but Identity:FixedUser is empty.", ct);
        }
        if (mode.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveAsync(windowsAccount, "Windows sign-in is required. Enable Windows authentication for this application in IIS.", ct);
        }
        if (mode.Equals("CrmProof", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveAsync(windowsAccount, "Sign-in is required: the browser must prove who the user is through the CRM first.", ct);
        }
        return new CallerResult(null, "User identity is not configured (set Identity:Mode). The service refuses to run queries without knowing who is asking.");
    }

    /// <summary>
    /// Returns the account name for the admin panel, which needs no CRM lookup, using the same rules.
    /// </summary>
    public string? GetAccount(string? windowsAccount)
    {
        var mode = (_options.Mode ?? "").Trim();
        string? account = null;
        if (mode.Equals("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            account = _options.FixedUser;
        }
        else if (mode.Equals("Windows", StringComparison.OrdinalIgnoreCase) || mode.Equals("CrmProof", StringComparison.OrdinalIgnoreCase))
        {
            account = windowsAccount;
        }
        var name = (account ?? "").Trim();
        return name.Length == 0 ? null : name;
    }

    private async Task<CallerResult> ResolveAsync(string? account, string missingMessage, CancellationToken ct)
    {
        var name = (account ?? "").Trim();
        if (name.Length == 0)
        {
            return new CallerResult(null, missingMessage);
        }
        var id = await _resolver.ResolveAsync(name, ct);
        if (id is null)
        {
            return new CallerResult(null, $"'{name}' is not an active CRM user.");
        }
        return new CallerResult(Caller.User(id.Value), null);
    }
}
