using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMSWebUI.Models.DashBoard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMSWebUI.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            string tenantName=HttpContext.Session.GetString("TenantName");
            string tenanStore=HttpContext.Session.GetString("TenantStore");
            TenantInfoDto tenantInfoDto = new TenantInfoDto();
            tenantInfoDto.TenantName=tenantName;
             tenantInfoDto.TenantStore=tenanStore;
            return View(tenantInfoDto);
        }
    }
}