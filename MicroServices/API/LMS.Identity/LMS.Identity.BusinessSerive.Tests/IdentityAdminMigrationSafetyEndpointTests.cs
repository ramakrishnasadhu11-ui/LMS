using System.Collections.Generic;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class IdentityAdminMigrationSafetyEndpointTests
    {
        [Fact]
        public void AdminMigrate_ReturnsForbidden_InProductionEvenWhenEnabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true",
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:EnableStartupSeedingOutsideDevelopment"] = "true",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDataSources:0"] = "prod-server",
                ["DatabaseInitialization:AllowedNonDevelopmentSeedingDatabases:0"] = "prod-db",
                ["ConnectionStrings:IdentityDb"] = "Server=prod-server;Database=prod-db;User Id=u;Password=p;"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Production" });

            var result = controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        [Fact]
        public void AdminMigrate_ReturnsBadRequest_WhenConnectionIsUnsafe()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["ConnectionStrings:IdentityDb"] = "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;",
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true",
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:EnableStartupSeedingInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "IdentityDb_Dev",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentSeedingDatabases:0"] = "IdentityDb_Dev"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Development" });

            var result = controller.Migrate();

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        }

        [Fact]
        public void AdminMigrate_ReturnsForbidden_WhenEndpointOptInDisabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "false",
                ["ConnectionStrings:IdentityDb"] = "Server=(localdb)\\MSSQLLocalDB;Database=IdentityDb_Dev;Trusted_Connection=True;"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Development" });

            var result = controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        private static IConfiguration BuildConfig(IDictionary<string, string> values)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        private static AdminController BuildController(IConfiguration configuration, IHostEnvironment environment)
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>().Options;
            var db = new IdentityDbContext(options);
            var controller = new AdminController(db, configuration);

            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton(environment);
            services.AddSingleton<IHostEnvironment>(environment);

            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };
            context.Features.Set<ISessionFeature>(new TestSessionFeature());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };

            return controller;
        }

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMS.Identity.WebApi";
            public string ContentRootPath { get; set; } = System.AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public ISession Session
            {
                get => null;
                set { }
            }
        }
    }
}
