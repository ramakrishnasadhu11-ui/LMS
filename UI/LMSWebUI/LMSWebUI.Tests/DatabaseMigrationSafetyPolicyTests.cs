using System;
using System.Collections.Generic;
using LMSWebUI.Controllers;
using LMSWebUI.Data;
using LMSWebUI.Helpers;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace LMSWebUI.Tests
{
    public class DatabaseMigrationSafetyPolicyTests
    {
        [Fact]
        public void EvaluateStartupMigration_DeniesOutsideDevelopment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=LMSDB_Dev;Trusted_Connection=True;");

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
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=LMSDB_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("disabled", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_DeniesUnsafeConnectionTarget()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("DataSource", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupMigration_AllowsApprovedDevelopmentConnection()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupMigration(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=LMSDB_Dev;Trusted_Connection=True;");

            Assert.True(decision.IsAllowed);
            Assert.Equal("(localdb)\\MSSQLLocalDB", decision.DataSource, ignoreCase: true);
            Assert.Equal("LMSDB_Dev", decision.DatabaseName, ignoreCase: true);
        }

        [Fact]
        public void EvaluateStartupSeeding_DeniesUnsafeHostedTargetInDevelopment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("not approved", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupSeeding_DeniesDevelopmentWhenOptInDisabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingInDevelopment"] = "false",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=LMSDB_Dev;Trusted_Connection=True;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("disabled", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupSeeding_AllowsApprovedDevelopmentTarget_WhenExplicitlyEnabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Server=(localdb)\\MSSQLLocalDB;Database=LMSDB_Dev;Trusted_Connection=True;");

            Assert.True(decision.IsAllowed);
        }

        [Fact]
        public void EvaluateStartupSeeding_DeniesProductionByDefault()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingOutsideDevelopment"] = "false",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDataSources:0"] = "prod-approved-server",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDatabases:0"] = "prod-approved-db"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Server=prod-approved-server;Database=prod-approved-db;User Id=u;Password=p;");

            Assert.False(decision.IsAllowed);
            Assert.Contains("outside Development", decision.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EvaluateStartupSeeding_AllowsProductionOnlyWhenExplicitlyEnabledAndAllowlisted()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableStartupSeedingOutsideDevelopment"] = "true",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDataSources:0"] = "prod-approved-server",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDatabases:0"] = "prod-approved-db"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var decision = DatabaseMigrationSafetyPolicy.EvaluateStartupSeeding(
                config,
                env,
                "Server=prod-approved-server;Database=prod-approved-db;User Id=u;Password=p;");

            Assert.True(decision.IsAllowed);
        }

        [Fact]
        public async System.Threading.Tasks.Task AdminMigrate_ReturnsForbidden_WhenEndpointOptInDisabledAsync()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "false"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var controller = BuildAdminController(config, env);

            var result = await controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            Assert.Contains("Remote service migrations are not supported", SerializeObject(objectResult.Value), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async System.Threading.Tasks.Task AdminMigrate_ReturnsBadRequest_WhenConnectionIsUnsafeAsync()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;",
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true",
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "LMSDB_Dev"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Development" };
            var controller = BuildAdminController(config, env);

            var result = await controller.Migrate();

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
            Assert.Contains("Remote service migrations are not supported", SerializeObject(objectResult.Value), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async System.Threading.Tasks.Task AdminMigrate_ReturnsForbidden_InProductionEnvironment()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true"
            });

            var env = new TestHostEnvironment { EnvironmentName = "Production" };
            var controller = BuildAdminController(config, env);

            var result = await controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        private static IConfiguration BuildConfig(IDictionary<string, string> values)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        private static string SerializeObject(object value)
        {
            return value == null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(value);
        }

        private static AdminController BuildAdminController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
            var db = new ApplicationDbContext(dbOptions);

            var userStore = new Mock<IUserStore<ApplicationUser>>();
            var userManager = new Mock<UserManager<ApplicationUser>>(
                userStore.Object,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);

            var roleStore = new Mock<IRoleStore<IdentityRole>>();
            var roleManager = new Mock<RoleManager<IdentityRole>>(
                roleStore.Object,
                null,
                null,
                null,
                null);

            var controller = new AdminController(db, userManager.Object, roleManager.Object);

            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton(environment);
            services.AddSingleton<IHostEnvironment>((IHostEnvironment)environment);
            services.AddHttpClient();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };
            httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(new TestSession()));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            return controller;
        }

        private sealed class TestHostEnvironment : IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMSWebUI";
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public TestSessionFeature(ISession session)
            {
                Session = session;
            }

            public ISession Session { get; set; }
        }

        private sealed class TestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> Keys => _store.Keys;
            public string Id => Guid.NewGuid().ToString("N");
            public bool IsAvailable => true;

            public void Clear() => _store.Clear();

            public System.Threading.Tasks.Task CommitAsync(System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.Task.CompletedTask;

            public System.Threading.Tasks.Task LoadAsync(System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.Task.CompletedTask;

            public void Remove(string key) => _store.Remove(key);

            public void Set(string key, byte[] value) => _store[key] = value;

            public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value);
        }
    }
}