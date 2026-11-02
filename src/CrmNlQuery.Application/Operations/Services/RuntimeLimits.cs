using CrmNlQuery.Application.Operations.Settings;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Application.Operations.Services;

/// <summary>
/// Provides the live value of the limits, reading the latest runtime setting on each access.
/// </summary>
public sealed class RuntimeLimits : ILimitsSource
{
    private readonly RuntimeSettings _settings;

    public RuntimeLimits(RuntimeSettings settings)
    {
        _settings = settings;
    }

    public GuardrailLimits Current => _settings.Limits;
}
