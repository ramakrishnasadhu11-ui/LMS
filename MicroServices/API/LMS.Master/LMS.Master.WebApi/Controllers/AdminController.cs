using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LMS.Master.BusinessSerive.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LMS.Master.WebApi.Controllers
{
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly MasterDbContext _db;
        private readonly IConfiguration _configuration;

        public AdminController(MasterDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpPost]
        [Route("admin/migrate")]
        [Authorize]
        public IActionResult Migrate()
        {
            var environment = HttpContext.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

            if (environment == null)
            {
                return StatusCode(500, new { success = false, error = "Migration safeguards could not be evaluated." });
            }

            if (!DatabaseInitializationSafetyPolicy.IsAdminMigrationEndpointEnabled(_configuration, environment))
            {
                return StatusCode(403, new
                {
                    success = false,
                    error = "Admin migration endpoint is disabled for this environment."
                });
            }

            var migrationDecision = DatabaseInitializationSafetyPolicy.EvaluateMigration(
                _configuration,
                environment,
                _configuration.GetConnectionString("MasterDb"));

            if (!migrationDecision.IsAllowed)
            {
                return BadRequest(new
                {
                    success = false,
                    error = migrationDecision.Reason,
                    migrationDecision.DataSource,
                    migrationDecision.DatabaseName
                });
            }

            try
            {
                _db.Database.Migrate();

                return Ok(new { success = true, message = "Master DB migrated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
