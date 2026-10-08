using LMSWebUI.Models.DashBoard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMSWebUI.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult MyDashboard()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var tenantStore = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantStore))
            {
                return RedirectToAction("Login", "Login");
            }

            return RedirectToAction(nameof(Index));
        }

        public IActionResult Index(string searchMode = "customer")
        {
            string tenantName = HttpContext.Session.GetString("TenantName");
            string tenanStore = HttpContext.Session.GetString("TenantStore");
            string userRole = HttpContext.Session.GetString("UserRole");
            var tenantInfoDto = new TenantInfoDto
            {
                TenantName = tenantName,
                TenantStore = tenanStore
            };

            var isSuperAdminUser = string.Equals(userRole, "SuperAdmin", System.StringComparison.OrdinalIgnoreCase);
            var isAdminUser = string.Equals(userRole, "Admin", System.StringComparison.OrdinalIgnoreCase)
                              || isSuperAdminUser;
            ViewData["IsAdminUser"] = isAdminUser;
            ViewData["UserRole"] = string.IsNullOrWhiteSpace(userRole) ? "StoreUser" : userRole;
            ViewData["HideSidebar"] = true;
            ViewData["SearchMode"] = string.Equals(searchMode, "invoice", System.StringComparison.OrdinalIgnoreCase)
                ? "invoice"
                : "customer";
            ViewData["DashboardTitle"] = isSuperAdminUser
                ? "Super Admin Dashboard"
                : isAdminUser
                    ? $"Admin Dashboard - {tenanStore} Store"
                    : $"Welcome to {tenanStore} Store";

            return View(tenantInfoDto);
        }
    }
}