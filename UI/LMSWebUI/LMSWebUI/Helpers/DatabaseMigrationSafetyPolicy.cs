using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LMSWebUI.Helpers
{
    public sealed class DatabaseMigrationDecision
    {
        public bool IsAllowed { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string DataSource { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
    }

    public static class DatabaseMigrationSafetyPolicy
    {
        private const string SectionName = "DatabaseInitialization";

        public static bool IsAdminMigrationEndpointEnabled(IConfiguration configuration, IHostEnvironment environment)
        {
            return environment?.IsDevelopment() == true
                   && configuration.GetValue<bool?>($"{SectionName}:EnableAdminMigrateEndpointInDevelopment") == true;
        }

        public static DatabaseMigrationDecision EvaluateStartupMigration(IConfiguration configuration, IHostEnvironment environment, string connectionString)
        {
            if (environment?.IsDevelopment() != true)
            {
                return Deny("Automatic migrations are disabled outside Development environment.");
            }

            if (configuration.GetValue<bool?>($"{SectionName}:EnableAutomaticMigrationsInDevelopment") != true)
            {
                return Deny("Automatic migrations are disabled. Enable DatabaseInitialization:EnableAutomaticMigrationsInDevelopment to opt in.");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Deny("Default database connection string is missing.");
            }

            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                return Deny($"Default database connection string is invalid: {ex.Message}");
            }

            var dataSource = (builder.DataSource ?? string.Empty).Trim();
            var databaseName = (builder.InitialCatalog ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(dataSource) || string.IsNullOrWhiteSpace(databaseName))
            {
                return Deny("Default connection must specify both DataSource and InitialCatalog.", dataSource, databaseName);
            }

            var allowedDataSources = GetConfiguredValues(configuration, $"{SectionName}:AllowedDevelopmentDataSources");
            if (allowedDataSources.Count == 0)
            {
                return Deny("AllowedDevelopmentDataSources is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDataSources, dataSource))
            {
                return Deny("Connection DataSource is not approved for Development migrations.", dataSource, databaseName);
            }

            var allowedDatabases = GetConfiguredValues(configuration, $"{SectionName}:AllowedDevelopmentDatabases");
            if (allowedDatabases.Count == 0)
            {
                return Deny("AllowedDevelopmentDatabases is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDatabases, databaseName))
            {
                return Deny("Connection InitialCatalog is not approved for Development migrations.", dataSource, databaseName);
            }

            return new DatabaseMigrationDecision
            {
                IsAllowed = true,
                Reason = "Migration safeguards passed.",
                DataSource = dataSource,
                DatabaseName = databaseName
            };
        }

        public static DatabaseMigrationDecision EvaluateStartupSeeding(IConfiguration configuration, IHostEnvironment environment, string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Deny("Default database connection string is missing.");
            }

            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                return Deny($"Default database connection string is invalid: {ex.Message}");
            }

            var dataSource = (builder.DataSource ?? string.Empty).Trim();
            var databaseName = (builder.InitialCatalog ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(dataSource) || string.IsNullOrWhiteSpace(databaseName))
            {
                return Deny("Default connection must specify both DataSource and InitialCatalog.", dataSource, databaseName);
            }

            var isDevelopment = environment?.IsDevelopment() == true;
            var seedingEnabled = isDevelopment
                ? configuration.GetValue<bool?>($"{SectionName}:EnableStartupSeedingInDevelopment") == true
                : configuration.GetValue<bool?>($"{SectionName}:EnableStartupSeedingOutsideDevelopment") == true;

            if (!seedingEnabled)
            {
                return Deny(
                    isDevelopment
                        ? "Startup seeding is disabled in Development. Enable DatabaseInitialization:EnableStartupSeedingInDevelopment to opt in."
                        : "Startup seeding is disabled outside Development. Use controlled deployment procedures.",
                    dataSource,
                    databaseName);
            }

            var dataSourceKey = isDevelopment
                ? $"{SectionName}:AllowedDevelopmentSeedingDataSources"
                : $"{SectionName}:AllowedNonDevelopmentSeedingDataSources";
            var databaseKey = isDevelopment
                ? $"{SectionName}:AllowedDevelopmentSeedingDatabases"
                : $"{SectionName}:AllowedNonDevelopmentSeedingDatabases";

            var allowedDataSources = GetConfiguredValues(configuration, dataSourceKey);
            if (allowedDataSources.Count == 0)
            {
                return Deny("Seeding datasource allowlist is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDataSources, dataSource))
            {
                return Deny("Connection DataSource is not approved for startup seeding.", dataSource, databaseName);
            }

            var allowedDatabases = GetConfiguredValues(configuration, databaseKey);
            if (allowedDatabases.Count == 0)
            {
                return Deny("Seeding database allowlist is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDatabases, databaseName))
            {
                return Deny("Connection InitialCatalog is not approved for startup seeding.", dataSource, databaseName);
            }

            return new DatabaseMigrationDecision
            {
                IsAllowed = true,
                Reason = "Seeding safeguards passed.",
                DataSource = dataSource,
                DatabaseName = databaseName
            };
        }

        private static DatabaseMigrationDecision Deny(string reason, string dataSource = "", string databaseName = "")
        {
            return new DatabaseMigrationDecision
            {
                IsAllowed = false,
                Reason = reason,
                DataSource = dataSource,
                DatabaseName = databaseName
            };
        }

        private static List<string> GetConfiguredValues(IConfiguration configuration, string key)
        {
            return configuration
                .GetSection(key)
                .Get<string[]>()
                ?.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private static bool ContainsInvariant(IEnumerable<string> values, string candidate)
        {
            return values.Any(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
        }
    }
}