using LMSWebUI.Models.DashBoard;
using LMSWebUI.Models;
using LMSWebUI.Services;
using LMSClientFactory.Helper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LMSWebUI.Controllers
{
    public class DashboardController : Controller
    {
        private const string AccessTokenSessionKey = "AccessToken";
        private const string OnboardingDismissedSessionKey = "TenantOnboardingDismissed";
        private const string OnboardingCompletedSessionKey = "TenantOnboardingCompleted";

        private readonly ILoginApiClient _loginApi;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(ILoginApiClient loginApi, IConfiguration configuration, ILogger<DashboardController> logger)
        {
            _loginApi = loginApi;
            _configuration = configuration;
            _logger = logger;
        }

        public IActionResult MyDashboard()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var tenantStore = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantStore))
            {
                return RedirectToAction("Login", "Login");
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Index(string searchMode = "customer")
        {
            string tenantName = HttpContext.Session.GetString("TenantName");
            string tenanStore = HttpContext.Session.GetString("TenantStore");
            string userRole = HttpContext.Session.GetString("UserRole");

            if (ShouldApplyTenantOnboardingGate(userRole))
            {
                var onboardingState = await GetTenantOnboardingStateAsync(tenantName);
                if (!onboardingState.IsDismissed)
                {
                    return RedirectToAction(nameof(Onboarding));
                }
            }

            var tenantInfoDto = new TenantInfoDto
            {
                TenantName = tenantName,
                TenantStore = tenanStore
            };

            var isSuperAdminUser = string.Equals(userRole, "SuperAdmin", System.StringComparison.OrdinalIgnoreCase);
            var isAdminUser = string.Equals(userRole, "Admin", System.StringComparison.OrdinalIgnoreCase)
                              || isSuperAdminUser;
            ViewData["IsAdminUser"] = isAdminUser;
            ViewData["UserRole"] = string.IsNullOrWhiteSpace(userRole) ? "StoreUser" : userRole;
            ViewData["HideSidebar"] = true;
            ViewData["SearchMode"] = string.Equals(searchMode, "invoice", System.StringComparison.OrdinalIgnoreCase)
                ? "invoice"
                : "customer";
            ViewData["DashboardTitle"] = isSuperAdminUser
                ? "Super Admin Dashboard"
                : isAdminUser
                    ? $"Admin Dashboard - {tenanStore} Store"
                    : $"Welcome to {tenanStore} Store";
            ViewData["CanResumeOnboarding"] = ShouldApplyTenantOnboardingGate(userRole);

            return View(tenantInfoDto);
        }

        [HttpGet]
        public async Task<IActionResult> Onboarding()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var tenantStore = HttpContext.Session.GetString("TenantStore");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantStore))
            {
                return RedirectToAction("Login", "Login");
            }

            if (!ShouldApplyTenantOnboardingGate(userRole))
            {
                return RedirectToAction(nameof(Index));
            }

            var profile = await GetTenantProfileAsync(tenantName);
            var onboardingState = await GetTenantOnboardingStateAsync(tenantName);

            DateTime? trialEndUtc = profile?.TrialEndUtc;
            if (!trialEndUtc.HasValue)
            {
                var claimValue = GetJwtClaimValue(HttpContext.Session.GetString(AccessTokenSessionKey), "trialEndUtc");
                if (DateTime.TryParse(claimValue, null, DateTimeStyles.RoundtripKind, out var parsedTrialEndUtc))
                {
                    trialEndUtc = parsedTrialEndUtc.ToUniversalTime();
                }
            }

            var subscriptionStatus = !string.IsNullOrWhiteSpace(profile?.SubscriptionStatus)
                ? profile.SubscriptionStatus
                : GetJwtClaimValue(HttpContext.Session.GetString(AccessTokenSessionKey), "subscriptionStatus");

            int? trialDaysRemaining = null;
            if (trialEndUtc.HasValue)
            {
                trialDaysRemaining = (int)Math.Ceiling((trialEndUtc.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            }

            var model = new TenantOnboardingViewModel
            {
                TenantName = string.IsNullOrWhiteSpace(profile?.TenantName)
                    ? (HttpContext.Session.GetString("TenantBusinessName") ?? tenantName)
                    : profile.TenantName,
                TenantStore = tenantStore,
                PlanCode = profile?.PlanCode,
                SubscriptionStatus = subscriptionStatus,
                TrialEndUtc = trialEndUtc,
                TrialDaysRemaining = trialDaysRemaining,
                IsOnboardingCompleted = onboardingState.IsCompleted
            };

            ViewData["OnboardingDemoUrl"] = _configuration["TenantOnboarding:DemoUrl"];
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SkipOnboarding()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(tenantName) || !ShouldApplyTenantOnboardingGate(userRole))
            {
                return RedirectToAction(nameof(Index));
            }

            var saveResult = await SaveTenantOnboardingStateAsync(tenantName, isCompleted: false);
            if (!saveResult.Success)
            {
                TempData["OnboardingError"] = string.IsNullOrWhiteSpace(saveResult.ErrorMessage)
                    ? "Unable to save your onboarding preference. Please try again."
                    : saveResult.ErrorMessage;
                return RedirectToAction(nameof(Onboarding));
            }

            var dismissedSessionKey = GetTenantScopedSessionKey(OnboardingDismissedSessionKey, tenantName);
            HttpContext.Session.SetString(dismissedSessionKey, "1");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteOnboarding()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrWhiteSpace(tenantName) || !ShouldApplyTenantOnboardingGate(userRole))
            {
                return RedirectToAction(nameof(Index));
            }

            var saveResult = await SaveTenantOnboardingStateAsync(tenantName, isCompleted: true);
            if (!saveResult.Success)
            {
                TempData["OnboardingError"] = string.IsNullOrWhiteSpace(saveResult.ErrorMessage)
                    ? "Unable to save your onboarding completion. Please try again."
                    : saveResult.ErrorMessage;
                return RedirectToAction(nameof(Onboarding));
            }

            var dismissedSessionKey = GetTenantScopedSessionKey(OnboardingDismissedSessionKey, tenantName);
            var completedSessionKey = GetTenantScopedSessionKey(OnboardingCompletedSessionKey, tenantName);

            HttpContext.Session.SetString(dismissedSessionKey, "1");
            HttpContext.Session.SetString(completedSessionKey, "1");
            return RedirectToAction(nameof(Index));
        }

        private bool ShouldApplyTenantOnboardingGate(string userRole)
        {
            var role = string.IsNullOrWhiteSpace(userRole) ? "StoreUser" : userRole.Trim();
            return !string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<LoginIoResponse> GetTenantProfileAsync(string tenantEmail)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return null;
            }

            var parameters = new Dictionary<string, string>
            {
                { "eMail", tenantEmail }
            };

            try
            {
                return await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parameters, Method.GET);
            }
            catch
            {
                return null;
            }
        }

        private async Task<(bool IsDismissed, bool IsCompleted)> GetTenantOnboardingStateAsync(string tenantEmail)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return (false, false);
            }

            var dismissedSessionKey = GetTenantScopedSessionKey(OnboardingDismissedSessionKey, tenantEmail);
            var completedSessionKey = GetTenantScopedSessionKey(OnboardingCompletedSessionKey, tenantEmail);

            var parameters = new Dictionary<string, string>
            {
                { "tenantEmail", tenantEmail },
                { "storeCode", string.Empty }
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", parameters, Method.GET);
                var cfgJson = GetTokenString(response, "ConfigurationJson")
                    ?? GetTokenString(GetResultSetObject(response), "ConfigurationJson");
                if (string.IsNullOrWhiteSpace(cfgJson))
                {
                    HttpContext.Session.Remove(dismissedSessionKey);
                    HttpContext.Session.Remove(completedSessionKey);
                    return (false, false);
                }

                var payload = JObject.Parse(cfgJson);
                var completedUtc = payload["OnboardingCompletedUtc"]?.ToString();
                var dismissedUtc = payload["OnboardingDismissedUtc"]?.ToString();
                var isCompleted = !string.IsNullOrWhiteSpace(completedUtc);
                var isDismissed = isCompleted || !string.IsNullOrWhiteSpace(dismissedUtc);

                if (isDismissed)
                {
                    HttpContext.Session.SetString(dismissedSessionKey, "1");
                }
                else
                {
                    HttpContext.Session.Remove(dismissedSessionKey);
                }

                if (isCompleted)
                {
                    HttpContext.Session.SetString(completedSessionKey, "1");
                }
                else
                {
                    HttpContext.Session.Remove(completedSessionKey);
                }

                return (isDismissed, isCompleted);
            }
            catch
            {
                // Fallback to tenant-scoped session state only when persisted lookup is unavailable.
                var sessionDismissed = string.Equals(HttpContext.Session.GetString(dismissedSessionKey), "1", StringComparison.Ordinal);
                var sessionCompleted = string.Equals(HttpContext.Session.GetString(completedSessionKey), "1", StringComparison.Ordinal);
                return (sessionDismissed, sessionCompleted);
            }
        }

        private async Task<(bool Success, string ErrorMessage)> SaveTenantOnboardingStateAsync(string tenantEmail, bool isCompleted)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return (false, "Tenant context is missing. Please log in again.");
            }

            JObject payload = null;

            var getParams = new Dictionary<string, string>
            {
                { "tenantEmail", tenantEmail },
                { "storeCode", string.Empty }
            };

            try
            {
                var existing = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", getParams, Method.GET);
                var cfgJson = GetTokenString(existing, "ConfigurationJson")
                    ?? GetTokenString(GetResultSetObject(existing), "ConfigurationJson");
                if (!string.IsNullOrWhiteSpace(cfgJson))
                {
                    payload = JObject.Parse(cfgJson);
                }
            }
            catch
            {
                payload = null;
            }

            payload ??= new JObject();
            payload["TenantName"] = tenantEmail;
            payload["StoreCode"] = string.Empty;

            if (isCompleted)
            {
                payload["OnboardingCompletedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }

            payload["OnboardingDismissedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            var request = new
            {
                TenantEmail = tenantEmail,
                StoreCode = string.Empty,
                ConfigurationJson = payload.ToString(Formatting.None),
                UpdatedBy = tenantEmail
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/NewStoreConfiguration", request, Method.POST);
                if (response == null)
                {
                    _logger?.LogWarning("Onboarding save returned null response for tenant {TenantEmail}.", tenantEmail);
                    return (false, "Onboarding service returned an empty response. Please try again.");
                }

                var (statusCode, message) = ParseApiStatus(response);
                var saveResponseShape = DescribeResponseShape(response);
                _logger?.LogInformation(
                    "Onboarding save response received for tenant {Tenant}. StatusCode={StatusCode}, HasMessage={HasMessage}, Shape={Shape}",
                    RedactTenant(tenantEmail),
                    statusCode ?? "<missing>",
                    !string.IsNullOrWhiteSpace(message),
                    saveResponseShape);

                if (!string.IsNullOrWhiteSpace(statusCode) && !string.Equals(statusCode, "200", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, string.IsNullOrWhiteSpace(message) ? "Unable to save onboarding state." : message);
                }

                var verifyResult = await VerifyOnboardingPersistenceAsync(tenantEmail, isCompleted);
                if (!verifyResult.Success)
                {
                    _logger?.LogWarning("Onboarding save verification failed for tenant {Tenant}. Reason={Reason}, Inconclusive={Inconclusive}", RedactTenant(tenantEmail), verifyResult.ErrorMessage, verifyResult.Inconclusive);

                    // Save call reported success, but read-back verification was inconclusive due
                    // downstream API issues. Do not treat this as persistence failure.
                    if (verifyResult.Inconclusive)
                    {
                        return (true, string.Empty);
                    }

                    return (false, verifyResult.ErrorMessage);
                }

                return (true, string.Empty);
            }
            catch (ApiUnavailableException ex)
            {
                _logger?.LogError(ex, "Onboarding save request failed for tenant {Tenant}. HttpStatusCode={HttpStatusCode}, ResponseBodyPresent={ResponseBodyPresent}", RedactTenant(tenantEmail), ex.StatusCode, !string.IsNullOrWhiteSpace(ex.ResponseContent));
                return (false, "Unable to reach the onboarding service. Please try again.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Onboarding save request failed for tenant {Tenant}.", RedactTenant(tenantEmail));
                return (false, "Unable to reach the onboarding service. Please try again.");
            }
        }

        private async Task<(bool Success, string ErrorMessage, bool Inconclusive)> VerifyOnboardingPersistenceAsync(string tenantEmail, bool isCompleted)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantEmail", tenantEmail },
                { "storeCode", string.Empty }
            };

            try
            {
                var persisted = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", parameters, Method.GET);
                if (persisted == null)
                {
                    _logger?.LogWarning("Onboarding verification returned null response for tenant {Tenant}.", RedactTenant(tenantEmail));
                    return (false, "Unable to confirm onboarding save. Please try again.", true);
                }

                var verifyStatus = ParseApiStatus(persisted);
                _logger?.LogInformation(
                    "Onboarding verification response for tenant {Tenant}. StatusCode={StatusCode}, Shape={Shape}",
                    RedactTenant(tenantEmail),
                    verifyStatus.StatusCode ?? "<missing>",
                    DescribeResponseShape(persisted));

                var cfgJson = GetTokenString(persisted, "ConfigurationJson")
                    ?? GetTokenString(GetResultSetObject(persisted), "ConfigurationJson");

                if (string.IsNullOrWhiteSpace(cfgJson))
                {
                    _logger?.LogWarning("Onboarding verification missing ConfigurationJson for tenant {Tenant}.", RedactTenant(tenantEmail));
                    return (false, "Unable to confirm onboarding save. Please try again.", false);
                }

                var j = JObject.Parse(cfgJson);
                var dismissedUtc = GetTokenString(j, "OnboardingDismissedUtc");
                var completedUtc = GetTokenString(j, "OnboardingCompletedUtc");

                var hasDismissed = !string.IsNullOrWhiteSpace(dismissedUtc);
                var hasCompleted = !string.IsNullOrWhiteSpace(completedUtc);

                var dismissedParsed = DateTime.TryParse(dismissedUtc, null, DateTimeStyles.RoundtripKind, out _);
                var completedParsed = DateTime.TryParse(completedUtc, null, DateTimeStyles.RoundtripKind, out _);

                _logger?.LogInformation(
                    "Onboarding verification flags for tenant {Tenant}. HasDismissed={HasDismissed}, DismissedParsed={DismissedParsed}, HasCompleted={HasCompleted}, CompletedParsed={CompletedParsed}, IsCompletedRequest={IsCompletedRequest}",
                    RedactTenant(tenantEmail),
                    hasDismissed,
                    dismissedParsed,
                    hasCompleted,
                    completedParsed,
                    isCompleted);

                if (!hasDismissed)
                {
                    return (false, "Your onboarding preference could not be saved. Please try again.", false);
                }

                if (isCompleted && !hasCompleted)
                {
                    return (false, "Your onboarding completion could not be saved. Please try again.", false);
                }

                return (true, string.Empty, false);
            }
            catch (ApiUnavailableException ex)
            {
                _logger?.LogWarning(ex, "Onboarding verification API unavailable for tenant {Tenant}. HttpStatusCode={HttpStatusCode}, ResponseBodyPresent={ResponseBodyPresent}", RedactTenant(tenantEmail), ex.StatusCode, !string.IsNullOrWhiteSpace(ex.ResponseContent));
                return (false, "Unable to confirm onboarding save. Please try again.", true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Onboarding save verification request failed for tenant {Tenant}.", RedactTenant(tenantEmail));
                return (false, "Unable to confirm onboarding save. Please try again.", true);
            }
        }

        private static (string StatusCode, string Message) ParseApiStatus(JObject response)
        {
            var rootStatus = GetTokenString(response, "StatusCode");
            var rootMessage = GetTokenString(response, "Message");

            if (!string.IsNullOrWhiteSpace(rootStatus) || !string.IsNullOrWhiteSpace(rootMessage))
            {
                return (rootStatus, rootMessage);
            }

            var resultSet = GetResultSetObject(response);
            if (resultSet != null)
            {
                return (GetTokenString(resultSet, "StatusCode"), GetTokenString(resultSet, "Message"));
            }

            return (null, null);
        }

        private static JObject GetResultSetObject(JObject source)
        {
            if (source == null)
            {
                return null;
            }

            var resultSet = source["ResultSet"] ?? source["resultSet"] ?? source["Result"] ?? source["result"];
            return resultSet as JObject;
        }

        private static string GetTokenString(JObject source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var direct = source[name];
            if (direct != null)
            {
                return direct.ToString();
            }

            var match = source.Properties()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return match?.Value?.ToString();
        }

        private static string DescribeResponseShape(JObject source)
        {
            if (source == null)
            {
                return "null";
            }

            var rootKeys = string.Join(",", source.Properties().Select(p => p.Name));
            var resultSetKeys = GetResultSetObject(source) is JObject rs
                ? string.Join(",", rs.Properties().Select(p => p.Name))
                : string.Empty;

            return string.IsNullOrWhiteSpace(resultSetKeys)
                ? $"root:[{rootKeys}]"
                : $"root:[{rootKeys}] resultSet:[{resultSetKeys}]";
        }

        private static string RedactTenant(string tenantEmail)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return "<empty>";
            }

            var value = tenantEmail.Trim();
            var atIndex = value.IndexOf('@');
            if (atIndex <= 1)
            {
                return "***";
            }

            return value.Substring(0, 2) + "***" + value.Substring(atIndex);
        }

        private static string GetJwtClaimValue(string jwtToken, string claimName)
        {
            if (string.IsNullOrWhiteSpace(jwtToken) || string.IsNullOrWhiteSpace(claimName))
            {
                return null;
            }

            var parts = jwtToken.Split('.');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                return null;
            }

            try
            {
                var payloadSegment = parts[1].Replace('-', '+').Replace('_', '/');
                var padding = payloadSegment.Length % 4;
                if (padding > 0)
                {
                    payloadSegment = payloadSegment.PadRight(payloadSegment.Length + (4 - padding), '=');
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadSegment));
                var payload = JObject.Parse(json);

                var exact = payload[claimName];
                if (exact != null)
                {
                    return exact.ToString();
                }

                var match = payload.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, claimName, StringComparison.OrdinalIgnoreCase));

                return match?.Value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string GetTenantScopedSessionKey(string baseKey, string tenantEmail)
        {
            var normalizedTenant = string.IsNullOrWhiteSpace(tenantEmail)
                ? string.Empty
                : tenantEmail.Trim().ToLowerInvariant();

            return string.IsNullOrWhiteSpace(normalizedTenant)
                ? baseKey
                : $"{baseKey}:{normalizedTenant}";
        }
    }
}