using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Abstractions;

/// <summary>
/// Tests a model server, without saving anything.
/// </summary>
public interface IModelProbe
{
    /// <summary>
    /// Checks that the server answers and the model exists, and returns one line per check.
    /// </summary>
    Task<IReadOnlyList<SetupCheck>> RunAsync(ModelProbeInput input, CancellationToken ct = default);
}
