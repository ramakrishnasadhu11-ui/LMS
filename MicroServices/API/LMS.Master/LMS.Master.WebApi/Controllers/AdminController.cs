using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LMS.Master.BusinessSerive.Data;

namespace LMS.Master.WebApi.Controllers
{
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly MasterDbContext _db;

        public AdminController(MasterDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [Route("admin/migrate")]
        public IActionResult Migrate()
        {
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

                return Ok(new { success = true, message = "Master DB migrated/created." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
