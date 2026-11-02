using CrmNlQuery.Application.Setup.Services;
using CrmNlQuery.Infrastructure.Persistence.Postgres.Setup;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Composition;

/// <summary>
/// Adds the settings stored by the setup wizard to the configuration, below environment variables and above appsettings.json.
/// </summary>
public static class AppConfigurationExtensions
{
    /// <summary>
    /// Adds the PostgreSQL-backed settings source; the encryption key is read from the key file when secrets need to be decrypted.
    /// </summary>
    public static IConfigurationBuilder AddAppConfiguration(this IConfigurationBuilder builder, string? connectionString, string keyFilePath)
    {
        return builder.Add(new AppConfigurationSource(() => PostgresAppConfigReader.Read(connectionString), () => ConfigKeyFile.Open(keyFilePath, createIfMissing: false)));
    }
}
