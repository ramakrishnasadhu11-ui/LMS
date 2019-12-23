using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        public IActionResult Login()
        {
            return View("../Login/Login");
        }

        public IActionResult Register()
        {
            return View("../Login/Register");   
        }
    }
}