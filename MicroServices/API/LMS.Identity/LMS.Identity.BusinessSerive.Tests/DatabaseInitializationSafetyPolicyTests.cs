using System;
using System.Collections.Generic;
using LMS.Identity.WebApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class DatabaseInitializationSafetyPolicyTests
    {
        [Fact]
        public void EvaluateStartupMigration_DeniesOutsideDevelopment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=IdentityDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("outside Development", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_DeniesWhenOptInDisabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "false",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=IdentityDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("disabled", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_AllowsApprovedDevelopmentConnection()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=IdentityDb_Dev;Trusted_Connection=True;");

            Assert.True(decision.IsAllowed);
            Assert.Equal("(localdb)\\MSSQLLocalDB", decision.DataSource, ignoreCase: true);
            Assert.Equal("IdentityDb_Dev", decision.DatabaseName, ignoreCase: true);
        }

        [Fact]
        public void EvaluateStartupMigration_DeniesMissingConnectionString()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(config, env, null);

            Assert.False(decision.IsAllowed);
            Assert.Contains("missing", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_DeniesMalformedConnectionString()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(config, env, "this-is-not-a-valid-connection-string");

            Assert.False(decision.IsAllowed);
            Assert.Contains("invalid", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_DeniesWhenAllowlistsMissing()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=IdentityDb_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("allow", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupSeeding_DeniesHostedTargetInDevelopment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDatabases:0"] = "IdentityDb_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseInitializationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("not approved", decision.Reason, StringComparison.OrdinalIgnoreCase);
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
            public string ApplicationName { get; set; } = "LMS.Identity.WebApi";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
