using CrmNlQuery.Application.Operations.Abstractions;

namespace CrmNlQuery.Application.Operations.Models;

/// <summary>
/// Fixed limits source with values from settings or defaults that never change; also used in tests and eval.
/// </summary>
public sealed class FixedLimits : ILimitsSource
{
    public static FixedLimits Default { get; } = new FixedLimits(new GuardrailLimits());

    public FixedLimits(GuardrailLimits limits)
    {
        Current = limits.Normalize();
    }

    public GuardrailLimits Current { get; }
}
