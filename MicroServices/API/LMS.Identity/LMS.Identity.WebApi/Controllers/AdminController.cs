using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LMS.Identity.BusinessSerive.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LMS.Identity.WebApi.Controllers
{
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IdentityDbContext _db;
        private readonly IConfiguration _configuration;

        public AdminController(IdentityDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpPost]
        [Route("admin/migrate")]
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

            var migrationDecision = DatabaseInitializationSafetyPolicy.EvaluateStartupMigration(
                _configuration,
                environment,
                _configuration.GetConnectionString("IdentityDb"));

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

            var seedingDecision = DatabaseInitializationSafetyPolicy.EvaluateStartupSeeding(
                _configuration,
                environment,
                _configuration.GetConnectionString("IdentityDb"));

            if (!seedingDecision.IsAllowed)
            {
                return BadRequest(new
                {
                    success = false,
                    error = seedingDecision.Reason,
                    seedingDecision.DataSource,
                    seedingDecision.DatabaseName
                });
            }

            try
            {
                _db.Database.Migrate();

                SuperAdminSeeder.Seed(_db, _configuration);

                var configuredEmail = _configuration["SuperAdmin:Email"]?.Trim();
                var seeded = !string.IsNullOrWhiteSpace(configuredEmail)
                             && _db.SuperAdmins.Any(x => x.Email == configuredEmail);

                return Ok(new
                {
                    success = true,
                    message = "Identity DB migrated.",
                    superAdminSeeded = seeded,
                    superAdminEmail = configuredEmail
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        [Route("admin/superadmin")]
        public IActionResult SuperAdminStatus([FromQuery] string email)
        {
            try
            {
                var resolvedEmail = string.IsNullOrWhiteSpace(email)
                    ? _configuration["SuperAdmin:Email"]?.Trim()
                    : email.Trim();

                if (string.IsNullOrWhiteSpace(resolvedEmail))
                {
                    return BadRequest(new { success = false, message = "Email is required." });
                }

                var normalizedEmail = resolvedEmail.ToLower();
                var superAdmin = _db.SuperAdmins.AsNoTracking()
                    .FirstOrDefault(x => x.Email != null && x.Email.ToLower() == normalizedEmail);
                if (superAdmin == null)
                {
                    return Ok(new { success = true, exists = false, email = resolvedEmail });
                }

                return Ok(new
                {
                    success = true,
                    exists = true,
                    email = superAdmin.Email,
                    displayName = superAdmin.DisplayName,
                    isActive = superAdmin.IsActive,
                    isPasswordChanged = superAdmin.IsPasswordChanged,
                    createdDate = superAdmin.CreatedDate,
                    lastLoginDate = superAdmin.LastLoginDate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

    }
}
