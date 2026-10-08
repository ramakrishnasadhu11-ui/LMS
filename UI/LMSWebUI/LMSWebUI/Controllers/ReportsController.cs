using LMSWebUI.Models.DashBoard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMSWebUI.Controllers
{
    public class ReportsController : Controller
    {
        public IActionResult Index()
        {
            string tenantName = HttpContext.Session.GetString("TenantName");
            string tenantStore = HttpContext.Session.GetString("TenantStore");
            string userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantStore))
            {
                return RedirectToAction("Login", "Login");
            }

            var isAdminUser = string.Equals(userRole, "Admin", System.StringComparison.OrdinalIgnoreCase);
            if (!isAdminUser)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantInfoDto = new TenantInfoDto
            {
                TenantName = tenantName,
                TenantStore = tenantStore
            };

            ViewData["IsAdminUser"] = true;
            ViewData["UserRole"] = userRole;
            ViewData["HideSidebar"] = true;

            return View(tenantInfoDto);
        }
    }
}
