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
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        private readonly ILoginApiClient _clientApi;
        private readonly ILogger<LoginController> _logger;
        private const string AllowedStoresSessionKey = "TenantAllowedStores";
        private const string MemberSinceSessionKey = "TenantMemberSince";
        private static readonly EmailAddressAttribute EmailValidator = new EmailAddressAttribute();

        public LoginController(ILoginApiClient clientAPI, ILogger<LoginController> logger)
        {
            _clientApi = clientAPI;
            _logger = logger;
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
            var sessionTenantEmail = HttpContext.Session.GetString("TenantName");
            var sessionTenantStore = HttpContext.Session.GetString("TenantStore");
            var sessionUserRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(sessionTenantEmail))
            {
                return RedirectToAction(nameof(Login));
            }

            var model = new TenantProfileViewModel
            {
                Email = sessionTenantEmail,
                TenantName = sessionTenantEmail,
                TenantStore = sessionTenantStore,
                UserRole = string.IsNullOrWhiteSpace(sessionUserRole) ? "StoreUser" : sessionUserRole,
                ProfileImageUrl = HttpContext.Session.GetString("TenantProfileImageUrl")
            };

            var paramsGetAllStoresByClient = new Dictionary<string, string>
            {
                { "eMail", sessionTenantEmail }
            };

            try
            {
                var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
                if (responseMessage != null)
                {
                    model.Email = string.IsNullOrWhiteSpace(responseMessage.Email) ? sessionTenantEmail : responseMessage.Email;
                    model.TenantName = string.IsNullOrWhiteSpace(responseMessage.TenantName) ? model.TenantName : responseMessage.TenantName;
                    model.PhoneNumber = responseMessage.PhoneNumber;
                    model.Address = responseMessage.Address;
                    model.Country = responseMessage.Country;
                    model.CreatedDate = responseMessage.CreatedDate;
                }
            }
            catch (ApiUnavailableException)
            {
                TempData["ProfileMessage"] = "Profile service is temporarily unavailable. Showing available session details.";
            }
            catch
            {
                TempData["ProfileMessage"] = "Unable to load complete profile details right now.";
            }

            return View("TenantProfileDetailsAsync", model);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<string> Login(string Email, string Password, string Store)
        {
            if (!string.IsNullOrWhiteSpace(Email))
            {
                try
                {
                    var parmsLogin = new TenantLoginRequest
                    {
                        EMail = Email,
                        Password = Password,
                        StoreCode = Store ?? string.Empty
                    };

                    var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantLogin", parmsLogin, RestSharp.Method.POST);

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
                        if (IsPasswordChangeRequiredMessage(responseMessage.Message))
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
                            HttpContext.Session.Remove(MemberSinceSessionKey);
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

                        if (IsTenantLoginSuccessMessage(responseMessage.Message))
                        {
                            var tenantSessionEmail = string.IsNullOrWhiteSpace(responseMessage.Email)
                                ? Email
                                : responseMessage.Email;

                            HttpContext.Session.SetString("TenantName", tenantSessionEmail);
                            HttpContext.Session.SetString("TenantStore", resolvedStore);
                            HttpContext.Session.SetString(AllowedStoresSessionKey, string.Join(",", allowedStores));
                            await SetTenantMemberSinceSessionAsync(tenantSessionEmail);
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

        private async Task SetTenantMemberSinceSessionAsync(string tenantEmail)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                HttpContext.Session.Remove(MemberSinceSessionKey);
                return;
            }

            var parms = new Dictionary<string, string>
            {
                { "eMail", tenantEmail.Trim() }
            };

            try
            {
                var profile = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, RestSharp.Method.GET);
                if (profile?.CreatedDate.HasValue == true)
                {
                    HttpContext.Session.SetString(MemberSinceSessionKey, profile.CreatedDate.Value.ToString("o", CultureInfo.InvariantCulture));
                }
                else
                {
                    HttpContext.Session.Remove(MemberSinceSessionKey);
                }
            }
            catch (ApiUnavailableException)
            {
                HttpContext.Session.Remove(MemberSinceSessionKey);
            }
            catch
            {
                HttpContext.Session.Remove(MemberSinceSessionKey);
            }
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
            tenantDto ??= new TenantDto();

            NormalizeRegisterInput(tenantDto);
            ModelState.Clear();
            TryValidateModel(tenantDto);

            if (!ModelState.IsValid)
            {
                return View(tenantDto);
            }

            try
            {
                responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/RegisterTenant", tenantDto, RestSharp.Method.POST);
                if (responseMessage != null && !string.IsNullOrEmpty(responseMessage.TenantId))
                {
                    tenantDto.Message = responseMessage.Message;
                    return View(tenantDto);
                }

                if (IsDuplicateEmailRegistrationError(responseMessage))
                {
                    ModelState.AddModelError(nameof(TenantDto.Email), "This email address is already registered.");
                }
                else if (TryMapRegisterApiErrorToField(responseMessage?.Message, out var fieldName))
                {
                    ModelState.AddModelError(fieldName, responseMessage.Message);
                }
                else if (!string.IsNullOrWhiteSpace(responseMessage?.Message))
                {
                    ModelState.AddModelError(string.Empty, responseMessage.Message);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Unable to complete registration. Please verify details and try again.");
                }

                return View(tenantDto);
            }
            catch (ApiUnavailableException)
            {
                ModelState.AddModelError(string.Empty, "Registration service is temporarily unavailable. Please try again.");
                return View(tenantDto);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Unable to complete registration. Please verify details and try again.");
                return View(tenantDto);
            }
        }

        private static bool IsDuplicateEmailRegistrationError(LoginIoResponse responseMessage)
        {
            if (responseMessage == null)
            {
                return false;
            }

            var statusCode = responseMessage.StatusCode?.Trim();
            if (string.Equals(statusCode, "208", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var message = responseMessage.Message ?? string.Empty;
            var hasDuplicateKeywords = message.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0
                && (message.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("registered", StringComparison.OrdinalIgnoreCase) >= 0);

            if (string.Equals(statusCode, "409", StringComparison.OrdinalIgnoreCase) && hasDuplicateKeywords)
            {
                return true;
            }

            if (message.IndexOf("tenant already registered", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return hasDuplicateKeywords;
        }

        private static bool IsPasswordChangeRequiredMessage(string message)
            => !string.IsNullOrWhiteSpace(message)
               && message.IndexOf("need to change password", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsTenantLoginSuccessMessage(string message)
            => !string.IsNullOrWhiteSpace(message)
               && message.IndexOf("tenant login successful", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void NormalizeRegisterInput(TenantDto tenantDto)
        {
            if (tenantDto == null)
            {
                return;
            }

            tenantDto.FamilyName = tenantDto.FamilyName?.Trim();
            tenantDto.MiddleName = tenantDto.MiddleName?.Trim();
            tenantDto.TenantName = tenantDto.TenantName?.Trim();
            tenantDto.Email = tenantDto.Email?.Trim();
            tenantDto.PhoneNumber = tenantDto.PhoneNumber?.Trim();
            tenantDto.NoOfStores = tenantDto.NoOfStores?.Trim();
        }

        private static bool TryMapRegisterApiErrorToField(string message, out string fieldName)
        {
            fieldName = null;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            if (message.IndexOf("first name", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fieldName = nameof(TenantDto.FamilyName);
                return true;
            }

            if (message.IndexOf("last name", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fieldName = nameof(TenantDto.MiddleName);
                return true;
            }

            if (message.IndexOf("company", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("tenant", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fieldName = nameof(TenantDto.TenantName);
                return true;
            }

            if (message.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fieldName = nameof(TenantDto.PhoneNumber);
                return true;
            }

            if (message.IndexOf("store", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fieldName = nameof(TenantDto.NoOfStores);
                return true;
            }

            return false;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllStoresByTenant(string tenantEmail)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return BadRequest("Tenant email is required.");
            }

            var normalizedTenantEmail = tenantEmail.Trim();
            if (!EmailValidator.IsValid(normalizedTenantEmail))
            {
                return BadRequest("Tenant email is invalid.");
            }

            var paramsGetAllStoresByClient = new Dictionary<string, String>
            {
                { "eMail", normalizedTenantEmail }
            };

            try
            {
                var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
                if (responseMessage != null && responseMessage.StatusCode == "200")
                {
                    // Super admins have no stores, so the caller needs the role to avoid
                    // mistaking the empty list for a store-user login.
                    if (string.Equals(responseMessage.UserRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                    {
                        return Content("superadmin");
                    }

                    if (responseMessage.Storecodes != null)
                    {
                        return Content(string.Join(",", responseMessage.Storecodes.ToArray()));
                    }
                }

                return Content(string.Empty);
            }
            catch (Exception primaryEx)
            {
                _logger.LogWarning(primaryEx, "Primary tenant store lookup endpoint failed. Trying fallback endpoint.");

                try
                {
                    // fallback path: older identity endpoint still used in some deployments
                    var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", paramsGetAllStoresByClient, RestSharp.Method.GET);
                    if (responseMessage != null && responseMessage.Storecodes != null)
                    {
                        return Content(string.Join(",", responseMessage.Storecodes.ToArray()));
                    }

                    return Content(string.Empty);
                }
                catch (ApiUnavailableException fallbackUnavailableEx)
                {
                    _logger.LogError(fallbackUnavailableEx, "Tenant store lookup is unavailable in both primary and fallback endpoints.");
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Store lookup service is unavailable. Please try again.");
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback tenant store lookup failed unexpectedly.");
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Store lookup service is unavailable. Please try again.");
                }
            }
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

                var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/TenantLogin", parmsLogin, RestSharp.Method.POST);
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
                    && IsPasswordChangeRequiredMessage(responseMessage.Message))
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
                    && IsTenantLoginSuccessMessage(responseMessage.Message))
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
                return Json(new { success = false, message = "Session expired. Please log in again." });
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
                }, RestSharp.Method.POST);

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

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/CheckTenantEmail", paramsEmail, RestSharp.Method.GET);
            if (responseMessage != null && responseMessage.StatusCode == "200")
            {
                return 1;
            }

            return 0;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<bool> CheckIsPasswordChangedByTenant(string Email)
        {
            var paramsEmail = new Dictionary<string, string>
            {
                { "eMail", Email }
            };

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/CheckIsPasswordChangedBytenant", paramsEmail, RestSharp.Method.GET);
            if (responseMessage != null && responseMessage.StatusCode == "200" && responseMessage.Message.ToLower() == "false")
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
            var email = Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return "Email address is required.";
            }

            if (!EmailValidator.IsValid(email))
            {
                return "Please enter a valid email address.";
            }

            var forgotPasswordDto = new ChangePasswordDto
            {
                Email = email
            };

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/forgotpassword", forgotPasswordDto, RestSharp.Method.POST);
            return responseMessage.Message;
        }


        [HttpPost]
        [AllowAnonymous]
        public async Task<string> ChangePassword(string Email, string NewPassword, string OldPassword, string ConfirmNewPassword)
        {
            var email = Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                return "Email address is required.";
            }

            if (!EmailValidator.IsValid(email))
            {
                return "Please enter a valid email address.";
            }

            if (string.IsNullOrWhiteSpace(OldPassword))
            {
                return "Current password is required.";
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                return "New password is required.";
            }

            if (string.IsNullOrWhiteSpace(ConfirmNewPassword))
            {
                return "Confirm new password is required.";
            }

            if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
            {
                return "Confirm new password must match new password.";
            }

            if (string.Equals(OldPassword, NewPassword, StringComparison.Ordinal))
            {
                return "New password must be different from current password.";
            }

            var changePasswordDto = new ChangePasswordDto
            {
                Email = email,
                NewPassword = NewPassword,
                OldPassword = OldPassword
            };

            var responseMessage = await _clientApi.SendRequestAsync<LoginIoResponse>("/changepassword", changePasswordDto, RestSharp.Method.POST);
            return responseMessage.Message;
        }
    }
}