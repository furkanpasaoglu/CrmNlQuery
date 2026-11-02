using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Web.Contracts.Setup;

/// <summary>
/// A model server to test together with the confirmations the wizard collected for it.
/// </summary>
public sealed record TestModelDto(ModelProbeInput Model, bool ConfirmLocalNetwork, bool ConfirmCloudLab);
