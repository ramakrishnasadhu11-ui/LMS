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
            ClientLoginDto ClientLoginDto = new ClientLoginDto();
            //APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/TestService", RestSharp.Method.GET);
            return View(ClientLoginDto);
          //  return View("../Login/Login");
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
        public async Task<ActionResult> Register(ClientDto ClientDto)
        {
            myResponse APIResponse = new myResponse();
            if(ClientDto!=null)
            {
                byte[] init_photo=new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 };
                ClientDto.Photo = init_photo;
                ClientDto.CreatedByUserId = 1;
                ClientDto.ModifiedByUserId = 1;
                ClientDto.Active = false;
                APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/RegisterClient", ClientDto, RestSharp.Method.POST);
                if(APIResponse.StatusCode==200 && Convert.ToInt32(APIResponse.Result.ToString())>0)
                    APIResponse.Message = "Client Registration Successfully.Please contact Administrator for Activation of your Store.";
                else if(APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) == -1)
                    APIResponse.Message = "Your E-Mail was Already Registered with us.Please check your e-mail for further processing.";
            }
            else
            {
                APIResponse.Message = "Invalid data for this operation";
            }
            ClientDto.Message = APIResponse.Message;
            return View(ClientDto);

        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<string> GetAllStoresByClient(string clientUrl)
        {
            myResponse APIResponse = new myResponse();
            Dictionary<string, string> paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", clientUrl }
                
            };

            if (!string.IsNullOrEmpty(clientUrl))
            {
                APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/GetClientStoreDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
            }

            return Convert.ToString(APIResponse.Result);

        }

        }
    }