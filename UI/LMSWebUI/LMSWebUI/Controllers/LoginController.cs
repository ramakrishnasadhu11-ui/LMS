using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMSClientFactory.Helper;
using LMSWebUI.Models;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;
using LMSWebUI.Models.Login;
using Microsoft.AspNetCore.Authorization;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        private readonly IHttpClientApi clientAPI;
        public LoginController(IHttpClientApi clientAPI)
        {
            this.clientAPI = clientAPI;
        }
        public async Task<IActionResult> Login()
        {
            myResponse APIResponse = new myResponse();
            APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/TestService", RestSharp.Method.GET);
            return View("../Login/Login");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            ClientDto ClientDto = new ClientDto();
            return View(ClientDto);
         //   return View("../Login/Register");   
        }
        [HttpPost]
        [AllowAnonymous]
        public ActionResult Register(ClientDto ClientDto)
        {

            return View(ClientDto);

        }
        }
    }