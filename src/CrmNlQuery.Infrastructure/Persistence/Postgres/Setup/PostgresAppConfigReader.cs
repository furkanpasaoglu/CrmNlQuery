using CrmNlQuery.Application.Setup.Models;
using Npgsql;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Setup;

/// <summary>
/// Reads the stored setup settings synchronously at startup, before the services exist; when the database or the table is not there yet it returns nothing.
/// </summary>
public static class PostgresAppConfigReader
{
    /// <summary>
    /// Returns the stored settings, or an empty list when the connection string is missing or the database cannot be read yet.
    /// </summary>
    public static IReadOnlyList<AppConfigEntry> Read(string? connectionString)
    {
        var entries = new List<AppConfigEntry>();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return entries;
        }
        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand("select key, value, secret from app_config", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new AppConfigEntry(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
            }
            return entries;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or System.IO.IOException or TimeoutException)
        {
            return new List<AppConfigEntry>();
        }
    }
}
