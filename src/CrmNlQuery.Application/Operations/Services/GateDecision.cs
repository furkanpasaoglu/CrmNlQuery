using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Result of the gate check: either allowed with a lease or denied with a reason.
/// </summary>
public sealed class GateDecision
{
    private GateDecision(GateLease? lease, GateDenial? denial)
    {
        Lease = lease;
        Denial = denial;
    }

    public GateLease? Lease { get; }
    public GateDenial? Denial { get; }
    public bool Allowed => Lease is not null;

    /// <summary>
    /// Creates an allowed decision holding the lease.
    /// </summary>
    internal static GateDecision Allow(GateLease lease) => new GateDecision(lease, null);
    /// <summary>
    /// Creates a denied decision holding the denial.
    /// </summary>
    internal static GateDecision Deny(GateDenial denial) => new GateDecision(null, denial);
}
