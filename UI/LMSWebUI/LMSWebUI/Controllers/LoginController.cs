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
using Newtonsoft.Json;
using System.Net.Http;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        private readonly IHttpClientApi clientAPI;
        public LoginController(IHttpClientApi clientAPI)
        {
            this.clientAPI = clientAPI;
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            ClientLoginDto ClientLoginDto = new ClientLoginDto();
            return View(ClientLoginDto);
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<string> Login(ClientLoginDto ClientLoginDto)
        {

            myResponse APIResponse = new myResponse();
            if(ClientLoginDto!=null)
            {
                //APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/ClientLogin", ClientLoginDto, RestSharp.Method.POST);
                //if (APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) ==1)
                //    APIResponse.Message = "Login Sucessfull.";
                //else if (APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) == 3)
                //    APIResponse.Message = "The Email supplied was not found.";
                //else if (APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) == 0)
                //    APIResponse.Message = "The Password supplied was not found.";
                //else if (APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) == 2)
                //    APIResponse.Message = "The Store Code supplied was not found.";
                //else if (APIResponse.StatusCode == 200 && Convert.ToInt32(APIResponse.Result.ToString()) == -1)
                //    APIResponse.Message = "Internal Server Error Please Try Again.";
            }
            else
            {
            //    APIResponse.Message = "Invalid data for this operation";
            }
            return "";
           // return APIResponse.Message;
          //  ClientLoginDto.Message = APIResponse.Message;
          // return View("~/Views/Login/Login.cshtml", ClientLoginDto);
          //return View(ClientLoginDto);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            TenantDto ClientDto = new TenantDto();
            return View(ClientDto);
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> Register(TenantDto TenantDto)
        {
            try
            {
            myResponse APIResponse = new myResponse();
            if(TenantDto!=null)
            {
                LoginIoResponse responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/RegisterTenant", TenantDto, RestSharp.Method.POST);
                //if (responseMessage.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                //{
                //    //string responseString = await responseMessage.Content.ReadAsStringAsync();
                //    //LoginIoResponse responseObj = JsonConvert.DeserializeObject<LoginIoResponse>(responseString);

                //}
                }
            else
            {
            }
            return View(TenantDto);
            }
            catch(Exception ex)
            {
                throw ex;
            }
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
            return "";
           // return Convert.ToString(APIResponse.Result);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<string> CheckClientEmail(string clientUrl)
        {
            myResponse APIResponse = new myResponse();
            Dictionary<string, string> paramsEmail = new Dictionary<string, string>
            {
                { "eMail", clientUrl }
            };
            if (!string.IsNullOrEmpty(clientUrl))
            {
                APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/CheckClientEmail", paramsEmail, RestSharp.Method.GET);
            }
            return "";
          //  return Convert.ToString(APIResponse.Result);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<bool> CheckIsPasswordChangedByclient(ClientLoginDto ClientLoginDto)
        {
            myResponse APIResponse = new myResponse();
            Dictionary<string, string> paramsEmail = new Dictionary<string, string>
            {
                { "eMail", ClientLoginDto.Email }
            };
            APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/CheckIsPasswordChangedByclient", paramsEmail, RestSharp.Method.GET);
            //if (Convert.ToBoolean(APIResponse.Result) == false)
            //{
            //    return false;
            // //   ClientLoginDto.ShowDialog = true;
            //   // return View("~/Views/Login/Login.cshtml", ClientLoginDto);
            //}
            //else
            //{
            //    return true;
            // //    ClientLoginDto.ShowDialog = false;
            ////    await Login(ClientLoginDto);
            //}
            return true;
         //   return View("~/Views/Login/Login.cshtml", ClientLoginDto);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<string> ChangePassword(string userEmail, string NewPassword, string OldPassword)
        {
            myResponse APIResponse = new myResponse();
            Dictionary<string, string> paramsChangePassword = new Dictionary<string, string>
            {
                { "userEmail", userEmail },
                { "NewPassword", NewPassword },
                { "OldPassword", OldPassword }
            };
            APIResponse = await clientAPI.SendRequestAsync<myResponse>("/api/Login/ChangePassword", paramsChangePassword, RestSharp.Method.GET);
            if(Convert.ToInt32(APIResponse.StatusCode)==200)
            {
                //var obj = APIResponse.Result;
                //string output = JsonConvert.SerializeObject(obj);
                //var deserializedObj = JsonConvert.DeserializeObject<myResponse>(output);
                //return deserializedObj.Message;
            }
            else
            {
                //var obj = APIResponse.Result;
                //string output = JsonConvert.SerializeObject(obj);
                //var deserializedObj = JsonConvert.DeserializeObject<myResponse>(output);
                //return deserializedObj.Message;
            }
            return "";
        }

    }
}