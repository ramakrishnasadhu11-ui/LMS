using System.Collections.Generic;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class AdminMigrationSafetyEndpointTests
    {
        [Fact]
        public void AdminMigrate_ReturnsForbidden_InProductionEvenWhenEnabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true",
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev",
                ["ConnectionStrings:MasterDb"] = "Server=prod-server;Database=prod-db;User Id=u;Password=p;"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Production" });
            var result = controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        [Fact]
        public void AdminMigrate_ReturnsForbidden_WhenEndpointDisabled()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "false",
                ["ConnectionStrings:MasterDb"] = "Server=(localdb)\\MSSQLLocalDB;Database=MasterDb_Dev;Trusted_Connection=True;"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Development" });
            var result = controller.Migrate();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        }

        [Fact]
        public void AdminMigrate_ReturnsBadRequest_WhenConnectionIsUnsafe()
        {
            var config = BuildConfig(new Dictionary<string, string>
            {
                ["DatabaseInitialization:EnableAdminMigrateEndpointInDevelopment"] = "true",
                ["DatabaseInitialization:EnableAutomaticMigrationsInDevelopment"] = "true",
                ["DatabaseInitialization:AllowedDevelopmentDataSources:0"] = "(localdb)\\MSSQLLocalDB",
                ["DatabaseInitialization:AllowedDevelopmentDatabases:0"] = "MasterDb_Dev",
                ["ConnectionStrings:MasterDb"] = "Data Source=sql9001.site4now.net;Initial Catalog=db_acff11_lmsdb;User Id=u;Password=p;"
            });

            var controller = BuildController(config, new TestHostEnvironment { EnvironmentName = "Development" });
            var result = controller.Migrate();

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        }

        private static IConfiguration BuildConfig(IDictionary<string, string> values)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
        }

        private static AdminController BuildController(IConfiguration configuration, IHostEnvironment environment)
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>().Options;
            var db = new MasterDbContext(options);
            var controller = new AdminController(db, configuration);

            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton(environment);
            services.AddSingleton<IHostEnvironment>(environment);

            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };

            return controller;
        }

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMS.Master.WebApi";
            public string ContentRootPath { get; set; } = System.AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
