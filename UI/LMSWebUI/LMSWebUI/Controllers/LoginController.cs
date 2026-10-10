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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json.Linq;

namespace LMSWebUI.Controllers
{
    public class LoginController : Controller
    {
        private readonly ILoginApiClient _clientApi;
        private readonly ILogger<LoginController> _logger;
        private readonly IWebHostEnvironment _environment;
        private const string AllowedStoresSessionKey = "TenantAllowedStores";
        private const string MemberSinceSessionKey = "TenantMemberSince";
        private const string ProfileImageSessionKey = "TenantProfileImageUrl";
        private const string TenantIdSessionKey = "TenantId";
        private const int MaxProfileImageBytes = 2 * 1024 * 1024;
        private static readonly EmailAddressAttribute EmailValidator = new EmailAddressAttribute();
        private static readonly HashSet<string> AllowedProfileImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png"
        };

        public LoginController(ILoginApiClient clientAPI, ILogger<LoginController> logger, IWebHostEnvironment environment)
        {
            _clientApi = clientAPI;
            _logger = logger;
            _environment = environment;
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
        public async Task<IActionResult> TenantProfileDetailsAsync(string tenantEmail, string returnUrl)
        {
            var sessionTenantEmail = HttpContext.Session.GetString("TenantName");
            var sessionTenantId = HttpContext.Session.GetString(TenantIdSessionKey);
            var sessionTenantStore = HttpContext.Session.GetString("TenantStore");
            var sessionUserRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(sessionTenantEmail) || !EmailValidator.IsValid(sessionTenantEmail))
            {
                return RedirectToAction(nameof(Login));
            }

            var model = new TenantProfileViewModel
            {
                Email = sessionTenantEmail,
                TenantName = sessionTenantEmail,
                TenantStore = sessionTenantStore,
                UserRole = string.IsNullOrWhiteSpace(sessionUserRole) ? "StoreUser" : sessionUserRole,
                ProfileImageUrl = HttpContext.Session.GetString(ProfileImageSessionKey)
            };

            SetTenantProfileImageSession(sessionTenantEmail, sessionTenantId);
            model.ProfileImageUrl = HttpContext.Session.GetString(ProfileImageSessionKey);

            ViewData["BackUrl"] = ResolveSafeProfileBackUrl(returnUrl);

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
                    model.PhoneNumber = ResolveProfilePhoneNumber(responseMessage);
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
        [ValidateAntiForgeryToken]
        public IActionResult UploadProfilePhoto(IFormFile profileImage, string returnUrl)
        {
            var sessionTenantEmail = HttpContext.Session.GetString("TenantName");
            var sessionTenantId = HttpContext.Session.GetString(TenantIdSessionKey);
            if (string.IsNullOrWhiteSpace(sessionTenantEmail) || !EmailValidator.IsValid(sessionTenantEmail))
            {
                return RedirectToAction(nameof(Login));
            }

            if (profileImage == null || profileImage.Length == 0)
            {
                TempData["ProfileMessage"] = "Please choose an image to upload.";
                return RedirectToAction("TenantProfileDetails", new { returnUrl });
            }

            if (profileImage.Length > MaxProfileImageBytes)
            {
                TempData["ProfileMessage"] = "Profile image must be 2 MB or smaller.";
                return RedirectToAction("TenantProfileDetails", new { returnUrl });
            }

            var extension = Path.GetExtension(profileImage.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedProfileImageExtensions.Contains(extension))
            {
                TempData["ProfileMessage"] = "Only JPG, JPEG, and PNG files are allowed.";
                return RedirectToAction("TenantProfileDetails", new { returnUrl });
            }

            var contentType = profileImage.ContentType?.Trim();
            if (!string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ProfileMessage"] = "Only JPG, JPEG, and PNG files are allowed.";
                return RedirectToAction("TenantProfileDetails", new { returnUrl });
            }

            if (!IsSupportedImageContent(profileImage, extension))
            {
                TempData["ProfileMessage"] = "Invalid image content. Please upload a valid JPG, JPEG, or PNG file.";
                return RedirectToAction("TenantProfileDetails", new { returnUrl });
            }

            var tenantKey = BuildTenantKey(sessionTenantEmail, sessionTenantId);
            var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "tenant-profiles", tenantKey);
            Directory.CreateDirectory(uploadsRoot);
            var safeFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var savedFilePath = Path.Combine(uploadsRoot, safeFileName);

            try
            {
                using (var outputStream = new FileStream(savedFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    profileImage.CopyTo(outputStream);
                }

                var uploadsRootFullPath = Path.GetFullPath(uploadsRoot);
                var savedFileFullPath = Path.GetFullPath(savedFilePath);
                var existingImageFiles = Directory
                    .GetFiles(uploadsRoot)
                    .Where(file => AllowedProfileImageExtensions.Contains(Path.GetExtension(file)))
                    .Select(Path.GetFullPath)
                    .Where(file => file.StartsWith(uploadsRootFullPath, StringComparison.OrdinalIgnoreCase))
                    .Where(file => !string.Equals(file, savedFileFullPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var existingFile in existingImageFiles)
                {
                    try
                    {
                        System.IO.File.Delete(existingFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove previous profile image for tenant key {TenantKey}.", tenantKey);
                    }
                }

                var relativeUrl = $"~/uploads/tenant-profiles/{tenantKey}/{safeFileName}";
                HttpContext.Session.SetString(ProfileImageSessionKey, relativeUrl);
                TempData["ProfileMessage"] = "Profile image uploaded successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save profile image for tenant key {TenantKey}.", tenantKey);
                TempData["ProfileMessage"] = "Unable to save profile image right now. Please try again.";
            }

            return RedirectToAction("TenantProfileDetails", new { returnUrl });
        }

        private static string ResolveProfilePhoneNumber(LoginIoResponse response)
        {
            if (response == null)
            {
                return null;
            }

            return new[]
            {
                response.PhoneNumber,
                response.MobileNo,
                response.ContactNo
            }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private string ResolveSafeProfileBackUrl(string returnUrl)
        {
            var fallbackUrl = Url.Action("Index", "Dashboard") ?? "/";
            var profileUrl = Url.Action("TenantProfileDetails", "Login") ?? "/Login/TenantProfileDetails";

            if (IsSafeLocalBackUrl(returnUrl, profileUrl))
            {
                return returnUrl;
            }

            if (TryGetLocalPathFromReferer(Request?.Headers["Referer"].ToString(), out var refererLocalUrl)
                && IsSafeLocalBackUrl(refererLocalUrl, profileUrl))
            {
                return refererLocalUrl;
            }

            return fallbackUrl;
        }

        private bool IsSafeLocalBackUrl(string candidateUrl, string profileUrl)
        {
            if (string.IsNullOrWhiteSpace(candidateUrl) || !Url.IsLocalUrl(candidateUrl))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(profileUrl)
                || !candidateUrl.StartsWith(profileUrl, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetLocalPathFromReferer(string refererHeaderValue, out string localPath)
        {
            localPath = null;
            if (string.IsNullOrWhiteSpace(refererHeaderValue))
            {
                return false;
            }

            if (!Uri.TryCreate(refererHeaderValue, UriKind.Absolute, out var refererUri))
            {
                return false;
            }

            localPath = $"{refererUri.AbsolutePath}{refererUri.Query}";
            return !string.IsNullOrWhiteSpace(localPath);
        }

        private void SetTenantProfileImageSession(string tenantEmail, string tenantId)
        {
            var persistedProfileImageUrl = ResolvePersistedProfileImageUrl(tenantEmail, tenantId);
            if (string.IsNullOrWhiteSpace(persistedProfileImageUrl))
            {
                HttpContext.Session.Remove(ProfileImageSessionKey);
                return;
            }

            HttpContext.Session.SetString(ProfileImageSessionKey, persistedProfileImageUrl);
        }

        private string ResolvePersistedProfileImageUrl(string tenantEmail, string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(_environment?.WebRootPath))
            {
                return null;
            }

            var ownerKey = BuildTenantKey(tenantEmail, tenantId);
            var primaryImageUrl = ResolveLatestImageRelativeUrlForKey(ownerKey);
            if (!string.IsNullOrWhiteSpace(primaryImageUrl))
            {
                return primaryImageUrl;
            }

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                // Backward compatibility for older uploads keyed only by email.
                var legacyOwnerKey = BuildTenantKey(tenantEmail, null);
                return ResolveLatestImageRelativeUrlForKey(legacyOwnerKey);
            }

            return null;
        }

        private string ResolveLatestImageRelativeUrlForKey(string ownerKey)
        {
            if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(_environment?.WebRootPath))
            {
                return null;
            }

            var tenantFolder = Path.Combine(_environment.WebRootPath, "uploads", "tenant-profiles", ownerKey);
            if (!Directory.Exists(tenantFolder))
            {
                return null;
            }

            var latestImagePath = Directory
                .GetFiles(tenantFolder)
                .Where(file => AllowedProfileImageExtensions.Contains(Path.GetExtension(file)))
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(latestImagePath))
            {
                return null;
            }

            var latestImageFileName = Path.GetFileName(latestImagePath);
            return $"~/uploads/tenant-profiles/{ownerKey}/{latestImageFileName}";
        }

        private static string BuildTenantKey(string tenantEmail, string tenantId)
        {
            var normalizedEmail = tenantEmail?.Trim().ToLowerInvariant() ?? string.Empty;
            var normalizedTenantId = tenantId?.Trim().ToLowerInvariant() ?? string.Empty;
            var normalized = string.IsNullOrWhiteSpace(normalizedTenantId)
                ? normalizedEmail
                : $"{normalizedTenantId}|{normalizedEmail}";

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool IsSupportedImageContent(IFormFile file, string extension)
        {
            if (file == null || file.Length == 0)
            {
                return false;
            }

            using var stream = file.OpenReadStream();
            Span<byte> signature = stackalloc byte[8];
            var bytesRead = stream.Read(signature);
            if (bytesRead < 4)
            {
                return false;
            }

            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return bytesRead >= 8
                    && signature[0] == 0x89
                    && signature[1] == 0x50
                    && signature[2] == 0x4E
                    && signature[3] == 0x47
                    && signature[4] == 0x0D
                    && signature[5] == 0x0A
                    && signature[6] == 0x1A
                    && signature[7] == 0x0A;
            }

            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return signature[0] == 0xFF
                    && signature[1] == 0xD8
                    && signature[2] == 0xFF;
            }

            return false;
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
                            if (!string.IsNullOrWhiteSpace(responseMessage.TenantId))
                            {
                                HttpContext.Session.SetString(TenantIdSessionKey, responseMessage.TenantId.Trim());
                            }
                            else
                            {
                                HttpContext.Session.Remove(TenantIdSessionKey);
                            }
                            HttpContext.Session.SetString("TenantStore", string.Empty);
                            HttpContext.Session.SetString(AllowedStoresSessionKey, string.Empty);
                            HttpContext.Session.Remove(MemberSinceSessionKey);
                            SetTenantProfileImageSession(superAdminEmail, HttpContext.Session.GetString(TenantIdSessionKey));
                            HttpContext.Session.SetString("UserRole", "SuperAdmin");
                            return "success";
                        }

                        var tenantSessionEmail = string.IsNullOrWhiteSpace(responseMessage.Email)
                            ? Email
                            : responseMessage.Email;

                        var allowedStores = await FilterEnabledStoresForTenantAsync(tenantSessionEmail, responseMessage.Storecodes ?? new List<string>());

                        var resolvedStore = !string.IsNullOrWhiteSpace(Store)
                            ? Store.Trim()
                        : (allowedStores.Count > 0
                            ? allowedStores[0]
                                : string.Empty);

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
                            HttpContext.Session.SetString("TenantName", tenantSessionEmail);
                            if (!string.IsNullOrWhiteSpace(responseMessage.TenantId))
                            {
                                HttpContext.Session.SetString(TenantIdSessionKey, responseMessage.TenantId.Trim());
                            }
                            else
                            {
                                HttpContext.Session.Remove(TenantIdSessionKey);
                            }
                            HttpContext.Session.SetString("TenantStore", resolvedStore);
                            HttpContext.Session.SetString(AllowedStoresSessionKey, string.Join(",", allowedStores));
                            SetTenantProfileImageSession(tenantSessionEmail, HttpContext.Session.GetString(TenantIdSessionKey));
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

                    var enabledStores = await FilterEnabledStoresForTenantAsync(normalizedTenantEmail, responseMessage.Storecodes ?? new List<string>());
                    return Content(string.Join(",", enabledStores));
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
                    if (responseMessage != null)
                    {
                        var enabledStores = await FilterEnabledStoresForTenantAsync(normalizedTenantEmail, responseMessage.Storecodes ?? new List<string>());
                        return Content(string.Join(",", enabledStores));
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
                    var tenantEmailForStores = string.IsNullOrWhiteSpace(responseMessage.Email)
                        ? email
                        : responseMessage.Email;

                    var stores = isSuperAdmin
                        ? new List<string>()
                        : await FilterEnabledStoresForTenantAsync(tenantEmailForStores, responseMessage.Storecodes ?? new List<string>());

                    // Credentials are valid - let the Login POST drive the change-password screen.
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        stores,
                        role = isSuperAdmin ? "SuperAdmin" : "Admin",
                        isStoreUser = false
                    });
                }

                if (responseMessage != null && responseMessage.StatusCode == "200"
                    && IsTenantLoginSuccessMessage(responseMessage.Message))
                {
                    var role = string.IsNullOrWhiteSpace(responseMessage.UserRole) ? "StoreUser" : responseMessage.UserRole.Trim();
                    var tenantEmailForStores = string.IsNullOrWhiteSpace(responseMessage.Email)
                        ? email
                        : responseMessage.Email;
                    var stores = await FilterEnabledStoresForTenantAsync(tenantEmailForStores, responseMessage.Storecodes ?? new List<string>());
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

        private async Task<List<string>> FilterEnabledStoresForTenantAsync(string tenantEmail, IEnumerable<string> candidateStores)
        {
            var normalizedTenantEmail = tenantEmail?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTenantEmail))
            {
                return new List<string>();
            }

            var normalizedCandidateStores = (candidateStores ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCandidateStores.Count == 0)
            {
                return normalizedCandidateStores;
            }

            var enabledStores = await GetEnabledStoreCodesForTenantAsync(normalizedTenantEmail);

            return normalizedCandidateStores
                .Where(x => enabledStores.Contains(x))
                .ToList();
        }

        private async Task<HashSet<string>> GetEnabledStoreCodesForTenantAsync(string tenantEmail)
        {
            var response = await _clientApi.SendRequestAsync<JObject>("/GetAllStoreStatuses", new Dictionary<string, string>(), RestSharp.Method.GET);

            var statusesToken = response?["StoreStatuses"] ?? response?["storeStatuses"] ?? new JArray();
            var statusesArray = statusesToken as JArray ?? statusesToken?["$values"] as JArray ?? new JArray();

            return statusesArray
                .Select(x => new
                {
                    StoreCode = (x?["StoreCode"] ?? x?["storeCode"])?.ToString()?.Trim(),
                    IsActive = (bool?)(x?["IsActive"] ?? x?["isActive"]) ?? false,
                    TenantName = (x?["TenantName"] ?? x?["tenantName"])?.ToString()?.Trim(),
                    TenantEmail = (x?["TenantEmail"] ?? x?["tenantEmail"])?.ToString()?.Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode)
                    && x.IsActive
                    && (string.Equals(x.TenantName, tenantEmail, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.TenantEmail, tenantEmail, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.StoreCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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