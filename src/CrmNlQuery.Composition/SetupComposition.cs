using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Services;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Setup;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Composition;

/// <summary>
/// Wires the first-run setup wizard: tells when setup is needed, builds the wizard service on the real database, key file and probes, and clears the stored settings for a reset.
/// </summary>
public static class SetupComposition
{
    /// <summary>
    /// Returns where the configuration encryption key is kept: Setup:KeyFile, otherwise config.key on the data volume (/data in the container).
    /// </summary>
    public static string KeyFilePath(IConfiguration configuration)
    {
        var configured = configuration["Setup:KeyFile"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        return Directory.Exists("/data") ? "/data/config.key" : Path.Combine(AppContext.BaseDirectory, "config.key");
    }

    /// <summary>
    /// Setup is needed when it has not been completed and no CRM password is configured; installations configured through environment variables skip it.
    /// </summary>
    public static bool Needed(IConfiguration configuration)
    {
        return !string.Equals(configuration["Setup:Completed"], "true", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(configuration["Crm:Password"]);
    }

    /// <summary>
    /// Builds the wizard service for the given one-time setup code.
    /// </summary>
    public static SetupService CreateService(IConfiguration configuration, string code)
    {
        var connection = configuration["History:ConnectionString"];
        var keyPath = KeyFilePath(configuration);
        var opened = new SemaphoreSlim(1, 1);
        IAppConfigStore? store = null;
        return new SetupService(
            code,
            () => true,
            async ct =>
            {
                if (string.IsNullOrWhiteSpace(connection)) { return null; }
                await opened.WaitAsync(ct);
                try { return store ??= await PostgresAppConfigStore.CreateAsync(connection, ct); }
                finally { opened.Release(); }
            },
            () => ConfigKeyFile.Open(keyPath, createIfMissing: true)!,
            new CrmSetupProbe(),
            new LlmSetupProbe());
    }

    /// <summary>
    /// Deletes every stored setup setting so the wizard runs again; the key file is kept.
    /// </summary>
    public static async Task ResetAsync(string? connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("History:ConnectionString is not set, so there is nothing to reset.");
        }
        var store = await PostgresAppConfigStore.CreateAsync(connectionString, ct);
        await store.DeleteAllAsync(ct);
    }
}
