using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Abstractions;

/// <summary>
/// Tests a CRM connection and the service account rights the product needs, without saving anything.
/// </summary>
public interface ICrmProbe
{
    /// <summary>
    /// Runs the checks and returns one line per check.
    /// </summary>
    Task<IReadOnlyList<SetupCheck>> RunAsync(CrmProbeInput input, CancellationToken ct = default);
}
