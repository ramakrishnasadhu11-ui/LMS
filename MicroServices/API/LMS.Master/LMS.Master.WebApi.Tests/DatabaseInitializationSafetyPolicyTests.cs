using System;
using System.Collections.Generic;
using LMS.Master.WebApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class DatabaseInitializationSafetyPolicyTests
    {
        [Fact]
        public void EvaluateMigration_DeniesOutsideDevelopment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=MasterDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("outside Development", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateMigration_DeniesWhenOptInDisabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "false",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=MasterDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("disabled", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateMigration_AllowsOnlyApprovedConnection()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=MasterDb_Dev;Trusted_Connection=True;");

            Assert.True(decision.IsAllowed);
        }

        [Fact]
        public void EvaluateMigration_DeniesMalformedConnectionString()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateMigration(config, env, "not-a-valid-connection");

            Assert.False(decision.IsAllowed);
            Assert.Contains("invalid", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateMigration_DeniesMissingAllowlist()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=MasterDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("AllowedDevelopment", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        private static IConfiguration BuildConfig(IDictionary<string, string> values)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMS.Master.WebApi";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
