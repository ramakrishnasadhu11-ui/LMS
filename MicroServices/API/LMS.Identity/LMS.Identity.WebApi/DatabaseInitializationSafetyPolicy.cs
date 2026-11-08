using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LMS.Identity.WebApi
{
    public sealed class DatabaseInitializationDecision
    {
        public bool IsAllowed { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string DataSource { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
    }

    public static class DatabaseInitializationSafetyPolicy
    {
        private const string SectionName = "DatabaseInitialization";

        public static bool IsAdminMigrationEndpointEnabled(IConfiguration configuration, IHostEnvironment environment)
        {
            return environment?.IsDevelopment() == true
                   && configuration.GetValue<bool?>($"{SectionName}:EnableAdminMigrateEndpointInDevelopment") == true;
        }

        public static DatabaseInitializationDecision EvaluateStartupMigration(IConfiguration configuration, IHostEnvironment environment, string connectionString)
        {
            if (environment?.IsDevelopment() != true)
            {
                return Deny("Automatic migrations are disabled outside Development environment.");
            }

            if (configuration.GetValue<bool?>($"{SectionName}:EnableAutomaticMigrationsInDevelopment") != true)
            {
                return Deny("Automatic migrations are disabled. Enable DatabaseInitialization:EnableAutomaticMigrationsInDevelopment to opt in.");
            }

            return EvaluateAllowedConnection(configuration, connectionString,
                $"{SectionName}:AllowedDevelopmentDataSources",
                $"{SectionName}:AllowedDevelopmentDatabases",
                "Connection DataSource is not approved for Development migrations.",
                "Connection InitialCatalog is not approved for Development migrations.");
        }

        public static DatabaseInitializationDecision EvaluateStartupSeeding(IConfiguration configuration, IHostEnvironment environment, string connectionString)
        {
            var isDevelopment = environment?.IsDevelopment() == true;
            var seedingEnabled = isDevelopment
                ? configuration.GetValue<bool?>($"{SectionName}:EnableStartupSeedingInDevelopment") == true
                : configuration.GetValue<bool?>($"{SectionName}:EnableStartupSeedingOutsideDevelopment") == true;

            if (!seedingEnabled)
            {
                return Deny(
                    isDevelopment
                        ? "Startup seeding is disabled in Development. Enable DatabaseInitialization:EnableStartupSeedingInDevelopment to opt in."
                        : "Startup seeding is disabled outside Development. Use controlled deployment procedures.");
            }

            return EvaluateAllowedConnection(
                configuration,
                connectionString,
                isDevelopment
                    ? $"{SectionName}:AllowedDevelopmentSeedingDataSources"
                    : $"{SectionName}:AllowedNonDevelopmentSeedingDataSources",
                isDevelopment
                    ? $"{SectionName}:AllowedDevelopmentSeedingDatabases"
                    : $"{SectionName}:AllowedNonDevelopmentSeedingDatabases",
                "Connection DataSource is not approved for startup seeding.",
                "Connection InitialCatalog is not approved for startup seeding.");
        }

        private static DatabaseInitializationDecision EvaluateAllowedConnection(
            IConfiguration configuration,
            string connectionString,
            string allowedDataSourcesKey,
            string allowedDatabasesKey,
            string dataSourceDenyReason,
            string databaseDenyReason)
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

            var allowedDataSources = GetConfiguredValues(configuration, allowedDataSourcesKey);
            if (allowedDataSources.Count == 0)
            {
                return Deny("Datasource allowlist is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDataSources, dataSource))
            {
                return Deny(dataSourceDenyReason, dataSource, databaseName);
            }

            var allowedDatabases = GetConfiguredValues(configuration, allowedDatabasesKey);
            if (allowedDatabases.Count == 0)
            {
                return Deny("Database allowlist is not configured.", dataSource, databaseName);
            }

            if (!ContainsInvariant(allowedDatabases, databaseName))
            {
                return Deny(databaseDenyReason, dataSource, databaseName);
            }

            return new DatabaseInitializationDecision
            {
                IsAllowed = true,
                Reason = "Database initialization safeguards passed.",
                DataSource = dataSource,
                DatabaseName = databaseName
            };
        }

        private static DatabaseInitializationDecision Deny(string reason, string dataSource = "", string databaseName = "")
        {
            return new DatabaseInitializationDecision
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
