using System;
using System.Threading.Tasks;
using LMSWebUI.Data;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using LMSWebUI.Helpers;

namespace LMSWebUI.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpPost]
        [Route("admin/migrate")]
        public async Task<IActionResult> Migrate()
        {
            const string remoteMigrationNotSupportedMessage = "Remote service migrations are not supported through this endpoint. Identity and Master migrations must run through their controlled deployment procedures.";
            var config = HttpContext.RequestServices.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration)) as Microsoft.Extensions.Configuration.IConfiguration;
            var environment = HttpContext.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

            if (config == null || environment == null)
            {
                return StatusCode(500, new { success = false, error = "Migration safeguards could not be evaluated." });
            }

            if (!DatabaseMigrationSafetyPolicy.IsAdminMigrationEndpointEnabled(config, environment))
            {
                return StatusCode(403, new
                {
                    success = false,
                    error = "Admin migration endpoint is disabled for this environment.",
                    remoteMigrationPolicy = remoteMigrationNotSupportedMessage
                });
            }

            var migrationDecision = DatabaseMigrationSafetyPolicy.EvaluateStartupMigration(
                config,
                environment,
                config.GetConnectionString("DefaultConnection"));

            if (!migrationDecision.IsAllowed)
            {
                return BadRequest(new
                {
                    success = false,
                    error = migrationDecision.Reason,
                    migrationDecision.DataSource,
                    migrationDecision.DatabaseName,
                    remoteMigrationPolicy = remoteMigrationNotSupportedMessage
                });
            }

            try
            {
                // Migrate local UI DB only after migration safety checks pass.
                _db.Database.Migrate();
                await DbInitializer.SeedAsync(_userManager, _roleManager);

                return Ok(new
                {
                    success = true,
                    ui = "attempted",
                    remoteMigrationPolicy = remoteMigrationNotSupportedMessage
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message, remoteMigrationPolicy = remoteMigrationNotSupportedMessage });
            }
        }
    }
}
