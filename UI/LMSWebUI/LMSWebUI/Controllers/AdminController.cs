using System;
using System.Threading.Tasks;
using LMSWebUI.Data;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            var httpFactory = HttpContext.RequestServices.GetService(typeof(System.Net.Http.IHttpClientFactory)) as System.Net.Http.IHttpClientFactory;
            var config = HttpContext.RequestServices.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration)) as Microsoft.Extensions.Configuration.IConfiguration;
            try
            {
                // Call Identity service migrate endpoint
                var identityUrl = config?["Services:Identity:BaseUrl"]?.TrimEnd('/') + "/admin/migrate";
                var masterUrl = config?["Services:Master:BaseUrl"]?.TrimEnd('/') + "/admin/migrate";

                var client = httpFactory?.CreateClient("migrationClient");
                var identityResult = new { success = true, message = "Skipped." };
                var masterResult = new { success = true, message = "Skipped." };

                if (!string.IsNullOrWhiteSpace(identityUrl) && client != null)
                {
                    try
                    {
                        var resp = await client.PostAsync(identityUrl, null);
                        identityResult = new { success = resp.IsSuccessStatusCode, message = resp.ReasonPhrase };
                    }
                    catch (Exception ex)
                    {
                        identityResult = new { success = false, message = ex.Message };
                    }
                }

                if (!string.IsNullOrWhiteSpace(masterUrl) && client != null)
                {
                    try
                    {
                        var resp = await client.PostAsync(masterUrl, null);
                        masterResult = new { success = resp.IsSuccessStatusCode, message = resp.ReasonPhrase };
                    }
                    catch (Exception ex)
                    {
                        masterResult = new { success = false, message = ex.Message };
                    }
                }

                // Optionally migrate local UI DB as well
                try
                {
                    try
                    {
                        _db.Database.Migrate();
                    }
                    catch
                    {
                        _db.Database.EnsureCreated();
                    }

                    await DbInitializer.SeedAsync(_userManager, _roleManager);
                }
                catch
                {
                    // swallow local migration/seeding errors and return combined result
                }

                return Ok(new { identity = identityResult, master = masterResult, ui = "attempted" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
