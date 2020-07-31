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
            LoginIoResponse responseMessage=new LoginIoResponse();
            try
            {
            myResponse APIResponse = new myResponse();
            if(TenantDto!=null)
            {
                    responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/RegisterTenant", TenantDto, RestSharp.Method.POST);
                    if (responseMessage!=null && !string.IsNullOrEmpty(responseMessage.TenantId))
                    {
                        TenantDto.Message=responseMessage.Message;
                        return View(TenantDto);            
                    }
                }
            else
            {
                    TenantDto.Message=responseMessage.Message;
                    return View(TenantDto);            
            }
            TenantDto.Message=responseMessage.Message;
            return View(TenantDto);            
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<string> GetAllStoresByTenant(string tenantEmail)
        {
             LoginIoResponse responseMessage=new LoginIoResponse();
            Dictionary<string, string> paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };
            if (!string.IsNullOrEmpty(tenantEmail))
            {
                 responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
                 if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                    return string.Join(",", responseMessage.Storecodes.ToArray());
                    }
            }
            return string.Join(",", responseMessage.Storecodes.ToArray());
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<int> CheckTenantEmail(string tenantEmail)
        {
            LoginIoResponse responseMessage=new LoginIoResponse();
            Dictionary<string, string> paramsEmail = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/CheckTenantEmail", paramsEmail, RestSharp.Method.GET);
                    if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                       return Convert.ToInt32(responseMessage.StatusCode);             
                    }
            return Convert.ToInt32(responseMessage.StatusCode);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<bool> CheckIsPasswordChangedByTenant(string Email)
        {
            LoginIoResponse responseMessage=new LoginIoResponse();
            Dictionary<string, string> paramsEmail = new Dictionary<string, string>
            {
                { "eMail", Email }
            };
           responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/CheckIsPasswordChangedBytenant", paramsEmail, RestSharp.Method.GET);
            if (responseMessage!=null && responseMessage.StatusCode=="200" && responseMessage.Message.ToLower()=="false")
            {
                 return false;
            }
            else
            {
                return true;
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<string> forgotpassword(string Email)
        {
            LoginIoResponse responseMessage=new LoginIoResponse();
            ChangePasswordDto forgotPasswordDto=new ChangePasswordDto(); 
            forgotPasswordDto.Email=Email;
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/forgotpassword", forgotPasswordDto, RestSharp.Method.POST);
             if (responseMessage!=null && responseMessage.StatusCode=="200")
            {
                 return responseMessage.Message;
            }
            else
            {
                return responseMessage.Message;
            }
        }


        [HttpPost]
        [AllowAnonymous]
        public async Task<string> ChangePassword(string Email, string NewPassword, string OldPassword)
        {
            LoginIoResponse responseMessage=new LoginIoResponse();
            ChangePasswordDto ChangePasswordDto=new ChangePasswordDto(); 
            ChangePasswordDto.Email=Email;
            ChangePasswordDto.NewPassword=NewPassword;
            ChangePasswordDto.OldPassword=OldPassword;
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/changepassword", ChangePasswordDto, RestSharp.Method.POST);
             if (responseMessage!=null && responseMessage.StatusCode=="200")
            {
                 return responseMessage.Message;
            }
            else
            {
                return responseMessage.Message;
            }
        }
    }
}