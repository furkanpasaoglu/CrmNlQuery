namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// Everything the wizard collects: the CRM connection, the model, the service address, the first administrator and the confirmations about where the model runs.
/// </summary>
public sealed record SetupInput(
    CrmProbeInput Crm,
    ModelProbeInput Model,
    string ServiceUrl,
    string AdminAccount,
    bool ConfirmLocalNetwork,
    bool ConfirmCloudLab);
