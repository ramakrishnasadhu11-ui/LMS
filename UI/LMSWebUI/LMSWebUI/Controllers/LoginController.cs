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
using Microsoft.AspNetCore.Http;

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
        public IActionResult Login()
        {
            ClientLoginDto ClientLoginDto = new ClientLoginDto();
            return View(ClientLoginDto);
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TenantProfileDetailsAsync(string tenantEmail)
        {
            LoginIoResponse responseMessage=new LoginIoResponse();
            Dictionary<string, string> paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };
            if (!string.IsNullOrEmpty(tenantEmail))
            {
                 responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
            }
            return View();
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<bool> Login(string Email,string Password,string Store)
        {
            myResponse APIResponse = new myResponse();
            if(!string.IsNullOrWhiteSpace(Email))
            {
            LoginIoResponse responseMessage=new LoginIoResponse();
            Dictionary<string, string> parmsLogin = new Dictionary<string, string>
            {
                { "eMail", Email },
                { "Password", Password }
            };
            responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/TenantLogin", parmsLogin, RestSharp.Method.GET);
                 if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                    HttpContext.Session.SetString("TenantName",Email);
                    HttpContext.Session.SetString("TenantStore",Store);
                    return true;
                    }
                 else
                {
                    return false;
                }
            }
            return false;
        }

        [HttpGet]
        public IActionResult Logout()
        {
            return RedirectToAction("Login");
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