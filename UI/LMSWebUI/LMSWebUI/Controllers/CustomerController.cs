using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMSWebUI.Models;
using LMSWebUI.Models.Customer;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace LMSWebUI.Controllers
{
    public class CustomerController : Controller
    {
       
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult AddCustomer(CustomerInfoDto CustomerInfoDto)
        { 

            TempData["UserMessage"]=JsonConvert.SerializeObject(new MessageDto() {  CssClassName = "alert-success", Title = "Success!", DisplayMessage = "Data Saved" });
            return RedirectToAction("Index","Customer"); 
        }
    }
}