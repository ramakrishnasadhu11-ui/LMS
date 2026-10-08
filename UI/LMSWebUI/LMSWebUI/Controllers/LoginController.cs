using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMSClientFactory.Helper;
using LMSWebUI.Models;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;
using LMSWebUI.Models.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        private readonly ILoginApiClient _clientApi;
        private const string AllowedStoresSessionKey = "TenantAllowedStores";

        public LoginController(ILoginApiClient clientAPI)
        {
            _clientApi = clientAPI;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            var clientLoginDto = new ClientLoginDto();
            return View(clientLoginDto);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TenantProfileDetailsAsync(string tenantEmail)
        {
            var responseMessage = new LoginIoResponse();
            var paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };

            if (!string.IsNullOrEmpty(tenantEmail))
            {
<<<<<<< Updated upstream
                 responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
=======
                responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.Get);
>>>>>>> Stashed changes
            }

            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<string> Login(string Email, string Password, string Store)
        {
            if (!string.IsNullOrWhiteSpace(Email))
            {
<<<<<<< Updated upstream
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
=======
                try
>>>>>>> Stashed changes
                {
                    var parmsLogin = new TenantLoginRequest
                    {
                        EMail = Email,
                        Password = Password,
                        StoreCode = Store ?? string.Empty
                    };

                    var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantLogin", parmsLogin, RestSharp.Method.Post);

                    // A 403 means the credentials were valid but the tenant/store is not approved.
                    // Surface the API message instead of a misleading "bad password" error.
                    if (responseMessage != null && responseMessage.StatusCode == "403")
                    {
                        var blockedMessage = string.IsNullOrWhiteSpace(responseMessage.Message)
                            ? "Your account is not approved yet. Please contact the administrator."
                            : responseMessage.Message;
                        return "notApproved|" + blockedMessage;
                    }

                    if (responseMessage != null && responseMessage.StatusCode == "200")
                    {
                        // A first-time login must reach the change-password screen regardless of
                        // store selection, so this is checked before the store rules below.
                        if (string.Equals(responseMessage.Message, "Need to change Password", StringComparison.OrdinalIgnoreCase))
                        {
                            return "requirePasswordChange";
                        }

                        // Super admins are global accounts with no store of their own, so the
                        // store selection rules below do not apply to them.
                        if (string.Equals(responseMessage.UserRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                        {
                            var superAdminEmail = string.IsNullOrWhiteSpace(responseMessage.Email)
                                ? Email
                                : responseMessage.Email;

                            HttpContext.Session.SetString("TenantName", superAdminEmail);
                            HttpContext.Session.SetString("TenantStore", string.Empty);
                            HttpContext.Session.SetString(AllowedStoresSessionKey, string.Empty);
                            HttpContext.Session.SetString("UserRole", "SuperAdmin");
                            return "success";
                        }

                    var allowedStores = (responseMessage.Storecodes ?? new List<string>())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                        var resolvedStore = !string.IsNullOrWhiteSpace(Store)
                            ? Store.Trim()
                        : (allowedStores.Count > 0
                            ? allowedStores[0]
                                : string.Empty);

                    if (allowedStores.Count == 0 && !string.IsNullOrWhiteSpace(resolvedStore))
                    {
                        allowedStores.Add(resolvedStore);
                    }

                        if (string.IsNullOrWhiteSpace(resolvedStore))
                        {
                            return "storeRequired";
                        }

                    if (!allowedStores.Contains(resolvedStore, StringComparer.OrdinalIgnoreCase))
                    {
                        return "invalidStore";
                    }

                        if (string.Equals(responseMessage.Message, "Tenant Login Successfully", StringComparison.OrdinalIgnoreCase))
                        {
                            var tenantSessionEmail = string.IsNullOrWhiteSpace(responseMessage.Email)
                                ? Email
                                : responseMessage.Email;

                            HttpContext.Session.SetString("TenantName", tenantSessionEmail);
                            HttpContext.Session.SetString("TenantStore", resolvedStore);
                            HttpContext.Session.SetString(AllowedStoresSessionKey, string.Join(",", allowedStores));
                            var role = string.IsNullOrWhiteSpace(responseMessage.UserRole) ? "StoreUser" : responseMessage.UserRole.Trim();
                            HttpContext.Session.SetString("UserRole", role);
                            return "success";
                        }
                    }

                    return "invalidCredentials";
                }
                catch (ApiUnavailableException)
                {
                    // The identity API is unreachable - do not report this as a bad password.
                    return "serviceUnavailable";
                }
                catch
                {
                    return "invalidCredentials";
                }
            }

            return "invalidCredentials";
        }

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Logout")]
        public IActionResult LogoutPost()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            var clientDto = new TenantDto();
            return View(clientDto);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> Register(TenantDto tenantDto)
        {
            var responseMessage = new LoginIoResponse();

            try
            {
<<<<<<< Updated upstream
            myResponse APIResponse = new myResponse();
            if(TenantDto!=null)
            {
                    responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/RegisterTenant", TenantDto, RestSharp.Method.POST);
                    if (responseMessage!=null && !string.IsNullOrEmpty(responseMessage.TenantId))
=======
                if (tenantDto != null)
                {
                    responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/RegisterTenant", tenantDto, RestSharp.Method.Post);
                    if (responseMessage != null && !string.IsNullOrEmpty(responseMessage.TenantId))
>>>>>>> Stashed changes
                    {
                        tenantDto.Message = responseMessage.Message;
                        return View(tenantDto);
                    }
                }

                tenantDto.Message = responseMessage.Message;
                return View(tenantDto);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<string> GetAllStoresByTenant(string tenantEmail)
        {
            var responseMessage = new LoginIoResponse();
            var paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };

            if (!string.IsNullOrEmpty(tenantEmail))
            {
<<<<<<< Updated upstream
                 responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
                 if (responseMessage!=null && responseMessage.StatusCode=="200")
=======
                try
                {
                    responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", paramsGetAllStoresByClient, RestSharp.Method.Get);
                    if (responseMessage != null && responseMessage.StatusCode == "200")
>>>>>>> Stashed changes
                    {
                        // Super admins have no stores, so the caller needs the role to avoid
                        // mistaking the empty list for a store-user login.
                        if (string.Equals(responseMessage.UserRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                        {
                            return "superadmin";
                        }

                        if (responseMessage.Storecodes != null)
                        {
                            return string.Join(",", responseMessage.Storecodes.ToArray());
                        }
                    }
                }
                catch
                {
                    // fallback path: older identity endpoint still used in some deployments
                    try
                    {
                        responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.Get);
                        if (responseMessage != null && responseMessage.Storecodes != null)
                        {
                            return string.Join(",", responseMessage.Storecodes.ToArray());
                        }
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> ResolveLoginStores(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Json(new
                {
                    success = false,
                    message = "Email and password are required.",
                    stores = new List<string>(),
                    role = string.Empty,
                    isStoreUser = false
                });
            }

            try
            {
                var parmsLogin = new TenantLoginRequest
                {
                    EMail = email,
                    Password = password,
                    StoreCode = string.Empty
                };

                var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantLogin", parmsLogin, RestSharp.Method.Post);
                var isSuperAdmin = responseMessage != null
                    && string.Equals(responseMessage.UserRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

                // The credentials are valid but the tenant/store is awaiting approval. The API
                // message explains why, so it must be surfaced instead of a bad-password error.
                if (responseMessage != null && responseMessage.StatusCode == "403")
                {
                    return Json(new
                    {
                        success = false,
                        message = string.IsNullOrWhiteSpace(responseMessage.Message)
                            ? "Your account is not approved yet. Please contact the administrator."
                            : responseMessage.Message,
                        stores = new List<string>(),
                        role = string.Empty,
                        isStoreUser = false
                    });
                }

                if (responseMessage != null && responseMessage.StatusCode == "200"
                    && string.Equals(responseMessage.Message, "Need to change Password", StringComparison.OrdinalIgnoreCase))
                {
                    // Credentials are valid - let the Login POST drive the change-password screen.
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        stores = responseMessage.Storecodes ?? new List<string>(),
                        role = isSuperAdmin ? "SuperAdmin" : "Admin",
                        isStoreUser = false
                    });
                }

                if (responseMessage != null && responseMessage.StatusCode == "200"
                    && string.Equals(responseMessage.Message, "Tenant Login Successfully", StringComparison.OrdinalIgnoreCase))
                {
                    var role = string.IsNullOrWhiteSpace(responseMessage.UserRole) ? "StoreUser" : responseMessage.UserRole.Trim();
                    var stores = responseMessage.Storecodes ?? new List<string>();
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        stores,
                        role,
                        isStoreUser = !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                                      && !string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                    });
                }

                return Json(new
                {
                    success = false,
                    message = "Login failed. Please check your email/password.",
                    stores = new List<string>(),
                    role = string.Empty,
                    isStoreUser = false
                });
            }
            catch (ApiUnavailableException)
            {
                return Json(new
                {
                    success = false,
                    message = "The login service is unavailable. Please try again shortly.",
                    stores = new List<string>(),
                    role = string.Empty,
                    isStoreUser = false
                });
            }
            catch
            {
                return Json(new
                {
                    success = false,
                    message = "Login failed. Please check your email/password.",
                    stores = new List<string>(),
                    role = string.Empty,
                    isStoreUser = false
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetStoreUserActiveStatus(string userEmail, bool isActive)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Only admin users can update store users." });
            }

            var tenantEmail = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return Json(new { success = false, message = "User email is required." });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<LoginIoResponse>("/SetStoreUserActiveStatus", new Dictionary<string, string>
                {
                    { "tenantEmail", tenantEmail },
                    { "storeCode", storeCode },
                    { "userEmail", userEmail.Trim() },
                    { "isActive", isActive ? "true" : "false" }
                }, RestSharp.Method.Post);

                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? (isActive ? "Store user activated successfully." : "Store user deactivated successfully.")
                });
            }
            catch (Exception ex)
            {
                var apiErrorMessage = ex?.InnerException?.Message ?? ex?.Message;
                return Json(new
                {
                    success = false,
                    message = string.IsNullOrWhiteSpace(apiErrorMessage)
                        ? "Unable to update store user status."
                        : $"Unable to update store user status. {apiErrorMessage}"
                });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<int> CheckTenantEmail(string tenantEmail)
        {
            var paramsEmail = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };
<<<<<<< Updated upstream
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/CheckTenantEmail", paramsEmail, RestSharp.Method.GET);
                    if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                       return Convert.ToInt32(responseMessage.StatusCode);             
                    }
            return Convert.ToInt32(responseMessage.StatusCode);
=======

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/CheckTenantEmail", paramsEmail, RestSharp.Method.Get);
            if (responseMessage != null && responseMessage.StatusCode == "200")
            {
                return 1;
            }

            return 0;
>>>>>>> Stashed changes
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<bool> CheckIsPasswordChangedByTenant(string Email)
        {
            var paramsEmail = new Dictionary<string, string>
            {
                { "eMail", Email }
            };
<<<<<<< Updated upstream
           responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/CheckIsPasswordChangedBytenant", paramsEmail, RestSharp.Method.GET);
            if (responseMessage!=null && responseMessage.StatusCode=="200" && responseMessage.Message.ToLower()=="false")
=======

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/CheckIsPasswordChangedBytenant", paramsEmail, RestSharp.Method.Get);
            if (responseMessage != null && responseMessage.StatusCode == "200" && responseMessage.Message.ToLower() == "false")
>>>>>>> Stashed changes
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
        public async Task<string> ForgotPassword(string Email)
        {
<<<<<<< Updated upstream
            LoginIoResponse responseMessage=new LoginIoResponse();
            ChangePasswordDto forgotPasswordDto=new ChangePasswordDto(); 
            forgotPasswordDto.Email=Email;
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/forgotpassword", forgotPasswordDto, RestSharp.Method.POST);
             if (responseMessage!=null && responseMessage.StatusCode=="200")
=======
            var forgotPasswordDto = new ChangePasswordDto
>>>>>>> Stashed changes
            {
                Email = Email
            };

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/forgotpassword", forgotPasswordDto, RestSharp.Method.Post);
            return responseMessage.Message;
        }


        [HttpPost]
        [AllowAnonymous]
        public async Task<string> ChangePassword(string Email, string NewPassword, string OldPassword)
        {
<<<<<<< Updated upstream
            LoginIoResponse responseMessage=new LoginIoResponse();
            ChangePasswordDto ChangePasswordDto=new ChangePasswordDto(); 
            ChangePasswordDto.Email=Email;
            ChangePasswordDto.NewPassword=NewPassword;
            ChangePasswordDto.OldPassword=OldPassword;
             responseMessage = await clientAPI.SendRequestAsync<LoginIoResponse>("/changepassword", ChangePasswordDto, RestSharp.Method.POST);
             if (responseMessage!=null && responseMessage.StatusCode=="200")
=======
            var changePasswordDto = new ChangePasswordDto
>>>>>>> Stashed changes
            {
                Email = Email,
                NewPassword = NewPassword,
                OldPassword = OldPassword
            };

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/changepassword", changePasswordDto, RestSharp.Method.Post);
            return responseMessage.Message;
        }
    }
}