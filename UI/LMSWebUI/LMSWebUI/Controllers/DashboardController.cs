using LMSWebUI.Models.DashBoard;
using LMSWebUI.Models;
using LMSWebUI.Services;
using LMSClientFactory.Helper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
        private readonly string _onboardingDiagnosticsLogPath;
        private static readonly SemaphoreSlim OnboardingDiagnosticsFileLock = new SemaphoreSlim(1, 1);

        public DashboardController(ILoginApiClient loginApi, IConfiguration configuration, ILogger<DashboardController> logger, IWebHostEnvironment hostEnvironment)
        {
            _loginApi = loginApi;
            _configuration = configuration;
            _logger = logger;
            var contentRoot = hostEnvironment?.ContentRootPath ?? Directory.GetCurrentDirectory();
            _onboardingDiagnosticsLogPath = Path.Combine(contentRoot, "App_Data", "OnboardingDiagnostics.log");
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

        public async Task<IActionResult> Index(string searchMode = "customer", bool skipOnboardingGate = false)
        {
            string tenantName = HttpContext.Session.GetString("TenantName");
            string tenanStore = HttpContext.Session.GetString("TenantStore");
            string userRole = HttpContext.Session.GetString("UserRole");
            var onboardingState = (IsDismissed: false, IsCompleted: false);
            var onboardingStateLoaded = false;

            if (!skipOnboardingGate && ShouldApplyTenantOnboardingGate(userRole))
            {
                onboardingState = await GetTenantOnboardingStateAsync(tenantName);
                onboardingStateLoaded = true;
                if (!onboardingState.IsDismissed)
                {
                    _logger?.LogInformation(
                        "Redirecting to onboarding from dashboard index for tenant {Tenant}. Reason=NotDismissed, IsCompleted={IsCompleted}",
                        RedactTenant(tenantName),
                        onboardingState.IsCompleted);
                    return RedirectToAction(nameof(Onboarding));
                }
            }
            else if (skipOnboardingGate)
            {
                _logger?.LogInformation("Bypassing onboarding gate from dashboard index navigation for tenant {Tenant}.", RedactTenant(tenantName));
            }

            var tenantInfoDto = new TenantInfoDto
            {
                TenantName = tenantName,
                TenantStore = tenanStore
            };

            var isSuperAdminUser = string.Equals(userRole, "SuperAdmin", System.StringComparison.OrdinalIgnoreCase);
            var isAdminUser = string.Equals(userRole, "Admin", System.StringComparison.OrdinalIgnoreCase)
                              || isSuperAdminUser;

            var shouldShowSubscriptionSpotlight = false;
            string subscriptionPlanCode = null;
            string normalizedSubscriptionStatus = null;
            if (!isSuperAdminUser && ShouldApplyTenantOnboardingGate(userRole))
            {
                if (!onboardingStateLoaded)
                {
                    onboardingState = await GetTenantOnboardingStateAsync(tenantName);
                    onboardingStateLoaded = true;
                }

                shouldShowSubscriptionSpotlight = onboardingState.IsDismissed;
                if (shouldShowSubscriptionSpotlight)
                {
                    var profile = await GetTenantProfileAsync(tenantName);
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

                    subscriptionPlanCode = profile?.PlanCode;
                    normalizedSubscriptionStatus = NormalizeSubscriptionStatus(subscriptionStatus, trialEndUtc);
                }
            }

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
            ViewData["ShowSubscriptionSpotlight"] = shouldShowSubscriptionSpotlight;
            ViewData["CurrentPlanCode"] = subscriptionPlanCode;
            ViewData["CurrentSubscriptionStatus"] = normalizedSubscriptionStatus;

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

            var normalizedSubscriptionStatus = NormalizeSubscriptionStatus(subscriptionStatus, trialEndUtc);
            var registeredDomains = ResolveRegisteredDomains(profile, tenantName);
            var backupServiceStatus = ResolveBackupServiceStatus(profile);
            var subscriptionExpiryUtc = ResolveSubscriptionExpiryUtc(subscriptionStatus, normalizedSubscriptionStatus, trialEndUtc);
            var checklistProgress = await ResolveOnboardingChecklistProgressAsync(tenantName, tenantStore);

            int? trialDaysRemaining = null;
            if (trialEndUtc.HasValue)
            {
                trialDaysRemaining = (int)Math.Ceiling((trialEndUtc.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            }

            var isTrialSubscription = string.Equals(normalizedSubscriptionStatus, "Trial", StringComparison.OrdinalIgnoreCase);
            var storeTrialEndUtc = isTrialSubscription ? trialEndUtc : null;
            var storeTrialDaysRemaining = isTrialSubscription ? trialDaysRemaining : (int?)null;

            var model = new TenantOnboardingViewModel
            {
                TenantName = string.IsNullOrWhiteSpace(profile?.TenantName)
                    ? (HttpContext.Session.GetString("TenantBusinessName") ?? tenantName)
                    : profile.TenantName,
                TenantStore = tenantStore,
                PlanCode = profile?.PlanCode,
                PlanName = profile?.PlanCode,
                SubscriptionStatus = normalizedSubscriptionStatus,
                AccountStatus = normalizedSubscriptionStatus,
                TrialEndUtc = trialEndUtc,
                SubscriptionExpiryUtc = subscriptionExpiryUtc,
                TrialDaysRemaining = trialDaysRemaining,
                RegisteredDomains = registeredDomains,
                BackupServiceStatus = backupServiceStatus,
                StorePlanCode = profile?.PlanCode,
                StoreSubscriptionStatus = normalizedSubscriptionStatus,
                StoreTrialEndUtc = storeTrialEndUtc,
                StoreTrialDaysRemaining = storeTrialDaysRemaining,
                IsBusinessStoreSettingsCompleted = checklistProgress.IsBusinessStoreSettingsCompleted,
                IsLaundryPricesCompleted = checklistProgress.IsLaundryPricesCompleted,
                IsBarcodeTagsCompleted = checklistProgress.IsBarcodeTagsCompleted,
                IsFirstCustomerCompleted = checklistProgress.IsFirstCustomerCompleted,
                IsFirstOrderCompleted = checklistProgress.IsFirstOrderCompleted,
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

            await WriteOnboardingDiagnosticsAsync(
                operation: nameof(SkipOnboarding),
                stage: "Requested",
                httpStatus: null,
                businessStatus: null,
                responseKeys: null,
                hasConfigurationJson: null,
                hasDismissedTimestamp: null,
                hasCompletedTimestamp: null);

            _logger?.LogInformation("SkipOnboarding requested for tenant {Tenant}.", RedactTenant(tenantName));

            if (string.IsNullOrWhiteSpace(tenantName) || !ShouldApplyTenantOnboardingGate(userRole))
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: nameof(SkipOnboarding),
                    stage: "SkippedInvalidTenantOrRole",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null);

                _logger?.LogWarning(
                    "SkipOnboarding skipped. TenantPresent={TenantPresent}, UserRole={UserRole}",
                    !string.IsNullOrWhiteSpace(tenantName),
                    string.IsNullOrWhiteSpace(userRole) ? "<empty>" : userRole);
                return RedirectToAction(nameof(Index));
            }

            var saveResult = await SaveTenantOnboardingStateAsync(tenantName, isCompleted: false, operationName: nameof(SkipOnboarding));
            if (!saveResult.Success)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: nameof(SkipOnboarding),
                    stage: "SaveFailed",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null);

                _logger?.LogWarning("SkipOnboarding persistence failed for tenant {Tenant}. Reason={Reason}", RedactTenant(tenantName), saveResult.ErrorMessage);
                TempData["OnboardingError"] = string.IsNullOrWhiteSpace(saveResult.ErrorMessage)
                    ? "Unable to save your onboarding preference. Please try again."
                    : saveResult.ErrorMessage;
                return RedirectToAction(nameof(Onboarding));
            }

            var dismissedSessionKey = GetTenantScopedSessionKey(OnboardingDismissedSessionKey, tenantName);
            HttpContext.Session.SetString(dismissedSessionKey, "1");
            await WriteOnboardingDiagnosticsAsync(
                operation: nameof(SkipOnboarding),
                stage: "Succeeded",
                httpStatus: "200",
                businessStatus: "200",
                responseKeys: null,
                hasConfigurationJson: true,
                hasDismissedTimestamp: true,
                hasCompletedTimestamp: false);
            _logger?.LogInformation("SkipOnboarding succeeded for tenant {Tenant}. Redirecting to dashboard index.", RedactTenant(tenantName));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteOnboarding()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var userRole = HttpContext.Session.GetString("UserRole");

            await WriteOnboardingDiagnosticsAsync(
                operation: nameof(CompleteOnboarding),
                stage: "Requested",
                httpStatus: null,
                businessStatus: null,
                responseKeys: null,
                hasConfigurationJson: null,
                hasDismissedTimestamp: null,
                hasCompletedTimestamp: null);

            _logger?.LogInformation("CompleteOnboarding requested for tenant {Tenant}.", RedactTenant(tenantName));

            if (string.IsNullOrWhiteSpace(tenantName) || !ShouldApplyTenantOnboardingGate(userRole))
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: nameof(CompleteOnboarding),
                    stage: "SkippedInvalidTenantOrRole",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null);

                _logger?.LogWarning(
                    "CompleteOnboarding skipped. TenantPresent={TenantPresent}, UserRole={UserRole}",
                    !string.IsNullOrWhiteSpace(tenantName),
                    string.IsNullOrWhiteSpace(userRole) ? "<empty>" : userRole);
                return RedirectToAction(nameof(Index));
            }

            var saveResult = await SaveTenantOnboardingStateAsync(tenantName, isCompleted: true, operationName: nameof(CompleteOnboarding));
            if (!saveResult.Success)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: nameof(CompleteOnboarding),
                    stage: "SaveFailed",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null);

                _logger?.LogWarning("CompleteOnboarding persistence failed for tenant {Tenant}. Reason={Reason}", RedactTenant(tenantName), saveResult.ErrorMessage);
                TempData["OnboardingError"] = string.IsNullOrWhiteSpace(saveResult.ErrorMessage)
                    ? "Unable to save your onboarding completion. Please try again."
                    : saveResult.ErrorMessage;
                return RedirectToAction(nameof(Onboarding));
            }

            var dismissedSessionKey = GetTenantScopedSessionKey(OnboardingDismissedSessionKey, tenantName);
            var completedSessionKey = GetTenantScopedSessionKey(OnboardingCompletedSessionKey, tenantName);

            HttpContext.Session.SetString(dismissedSessionKey, "1");
            HttpContext.Session.SetString(completedSessionKey, "1");
            await WriteOnboardingDiagnosticsAsync(
                operation: nameof(CompleteOnboarding),
                stage: "Succeeded",
                httpStatus: "200",
                businessStatus: "200",
                responseKeys: null,
                hasConfigurationJson: true,
                hasDismissedTimestamp: true,
                hasCompletedTimestamp: true);
            _logger?.LogInformation("CompleteOnboarding succeeded for tenant {Tenant}. Redirecting to dashboard index.", RedactTenant(tenantName));
            return RedirectToAction(nameof(Index));
        }

        private bool ShouldApplyTenantOnboardingGate(string userRole)
        {
            var role = string.IsNullOrWhiteSpace(userRole) ? "StoreUser" : userRole.Trim();
            return !string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool IsBusinessStoreSettingsCompleted, bool IsLaundryPricesCompleted, bool IsBarcodeTagsCompleted, bool IsFirstCustomerCompleted, bool IsFirstOrderCompleted)> ResolveOnboardingChecklistProgressAsync(string tenantName, string tenantStore)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenantStore))
            {
                return (false, false, false, false, false);
            }

            var isBusinessStoreSettingsCompleted = await HasStoreConfigurationDataAsync(tenantName, tenantStore);
            var isLaundryPricesCompleted = await HasLaundryPricesAsync(tenantName, tenantStore);
            var isBarcodeTagsCompleted = await HasBarcodeTagSettingsAsync(tenantName, tenantStore);
            var customerNames = await GetCustomerNamesAsync(tenantName, tenantStore);
            var isFirstCustomerCompleted = HasAtLeastOneCustomer(customerNames);
            var isFirstOrderCompleted = await HasAtLeastOneOrderAsync(tenantName, tenantStore, customerNames);

            return (
                isBusinessStoreSettingsCompleted,
                isLaundryPricesCompleted,
                isBarcodeTagsCompleted,
                isFirstCustomerCompleted,
                isFirstOrderCompleted);
        }

        private async Task<List<string>> GetCustomerNamesAsync(string tenantName, string tenantStore)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", tenantStore },
                { "eMail", string.Empty }
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetCustomers", parameters, Method.GET);
                return (GetToken(response, "CustomerNames") as JArray)
                    ?.Select(x => x?.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        private static bool HasAtLeastOneCustomer(IReadOnlyCollection<string> customerNames)
        {
            return customerNames != null && customerNames.Count > 0;
        }

        private async Task<bool> HasStoreConfigurationDataAsync(string tenantName, string tenantStore)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantEmail", tenantName },
                { "storeCode", tenantStore }
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", parameters, Method.GET);
                var cfgJson = ExtractConfigurationJson(response, out _);
                if (string.IsNullOrWhiteSpace(cfgJson))
                {
                    return false;
                }

                JObject payload;
                try
                {
                    payload = JObject.Parse(cfgJson);
                }
                catch
                {
                    return false;
                }

                var storeName = GetTokenString(payload, "StoreName");
                var contactPerson = GetTokenString(payload, "ContactPerson");
                var phoneNumber = GetTokenString(payload, "PhoneNumber");
                var email = GetTokenString(payload, "Email");
                var addressLine1 = GetTokenString(payload, "AddressLine1");
                var addressLine2 = GetTokenString(payload, "AddressLine2");
                var city = GetTokenString(payload, "City");
                var state = GetTokenString(payload, "State");
                var postalCode = GetTokenString(payload, "PostalCode");
                var openingTime = GetTokenString(payload, "OpeningTime");
                var closingTime = GetTokenString(payload, "ClosingTime");
                var notes = GetTokenString(payload, "Notes");

                var normalizedStoreName = (storeName ?? string.Empty).Trim();
                var normalizedStoreCode = (tenantStore ?? string.Empty).Trim();

                return !string.IsNullOrWhiteSpace(contactPerson)
                    || !string.IsNullOrWhiteSpace(phoneNumber)
                    || !string.IsNullOrWhiteSpace(email)
                    || !string.IsNullOrWhiteSpace(addressLine1)
                    || !string.IsNullOrWhiteSpace(addressLine2)
                    || !string.IsNullOrWhiteSpace(city)
                    || !string.IsNullOrWhiteSpace(state)
                    || !string.IsNullOrWhiteSpace(postalCode)
                    || !string.IsNullOrWhiteSpace(notes)
                    || (!string.IsNullOrWhiteSpace(normalizedStoreName) && !string.Equals(normalizedStoreName, normalizedStoreCode, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(openingTime) && !string.Equals(openingTime.Trim(), "09:00", StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(closingTime) && !string.Equals(closingTime.Trim(), "21:00", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HasLaundryPricesAsync(string tenantName, string tenantStore)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", tenantStore },
                { "includeInactive", "false" }
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JArray>("/GetLaundryItemPrices", parameters, Method.GET);
                return response != null && response.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HasBarcodeTagSettingsAsync(string tenantName, string tenantStore)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", tenantStore }
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetBarcodeTagSettings", parameters, Method.GET);
                if (response == null)
                {
                    return false;
                }

                if (response.TryGetValue("StatusCode", StringComparison.OrdinalIgnoreCase, out var statusToken)
                    && string.Equals(statusToken?.ToString(), "404", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HasAtLeastOneOrderAsync(string tenantName, string tenantStore, IReadOnlyCollection<string> customerNames)
        {
            try
            {
                if (customerNames == null || customerNames.Count == 0)
                {
                    return false;
                }

                foreach (var customerName in customerNames)
                {
                    var customerByNameResponse = await _loginApi.SendRequestAsync<JObject>("/GetCustomerByName", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", tenantStore },
                        { "customerName", customerName.Trim() }
                    }, Method.GET);

                    var custCode = GetTokenString(customerByNameResponse, "CustCode");
                    if (string.IsNullOrWhiteSpace(custCode))
                    {
                        continue;
                    }

                    var orderResponse = await _loginApi.SendRequestAsync<JObject>("/GetLaundryOrders", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", tenantStore },
                        { "custCode", custCode }
                    }, Method.GET);

                    var ordersToken = GetToken(orderResponse, "Orders") ?? GetToken(GetResultSetObject(orderResponse), "Orders");
                    if (ordersToken is JArray orders && orders.Count > 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
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
                var cfgJson = ExtractConfigurationJson(response, out _);
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

        private async Task<(bool Success, string ErrorMessage)> SaveTenantOnboardingStateAsync(string tenantEmail, bool isCompleted, string operationName)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "SaveMissingTenantContext",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null);
                return (false, "Tenant context is missing. Please log in again.");
            }

            _logger?.LogInformation(
                "Onboarding save started. Action={Action}, Tenant={Tenant}, HasTenantEmail={HasTenantEmail}, StoreCodeEmpty={StoreCodeEmpty}, IsCompletedRequest={IsCompletedRequest}",
                operationName,
                RedactTenant(tenantEmail),
                true,
                true,
                isCompleted);

            JObject payload = null;

            var getParams = new Dictionary<string, string>
            {
                { "tenantEmail", tenantEmail },
                { "storeCode", string.Empty }
            };

            try
            {
                var existing = await _loginApi.SendRequestAsync<JObject>("/GetStoreConfiguration", getParams, Method.GET);
                var cfgJson = ExtractConfigurationJson(existing, out _);
                if (!string.IsNullOrWhiteSpace(cfgJson))
                {
                    payload = JObject.Parse(cfgJson);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Onboarding save pre-read failed. Action={Action}, Tenant={Tenant}", operationName, RedactTenant(tenantEmail));
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
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "SaveNullResponse",
                        httpStatus: null,
                        businessStatus: null,
                        responseKeys: "null",
                        hasConfigurationJson: null,
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null);
                    _logger?.LogWarning("Onboarding save returned null response for tenant {TenantEmail}.", tenantEmail);
                    return (false, "Onboarding service returned an empty response. Please try again.");
                }

                var (statusCode, message) = ParseApiStatus(response);
                var saveResponseShape = DescribeResponseShape(response);
                _logger?.LogInformation(
                    "Onboarding save response received. Action={Action}, Tenant={Tenant}, StatusCode={StatusCode}, HasMessage={HasMessage}, Shape={Shape}",
                    operationName,
                    RedactTenant(tenantEmail),
                    statusCode ?? "<missing>",
                    !string.IsNullOrWhiteSpace(message),
                    saveResponseShape);

                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "SaveResponseReceived",
                    httpStatus: NormalizeStatusCode(GetTokenString(response, "StatusCode")),
                    businessStatus: statusCode,
                    responseKeys: saveResponseShape,
                    hasConfigurationJson: !string.IsNullOrWhiteSpace(ExtractConfigurationJson(response, out _)),
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null,
                    validationErrors: DescribeValidationErrors(response));

                if (!string.IsNullOrWhiteSpace(statusCode) && !string.Equals(statusCode, "200", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "SaveBusinessStatusFailure",
                        httpStatus: NormalizeStatusCode(GetTokenString(response, "StatusCode")),
                        businessStatus: statusCode,
                        responseKeys: saveResponseShape,
                        hasConfigurationJson: !string.IsNullOrWhiteSpace(ExtractConfigurationJson(response, out _)),
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null,
                        validationErrors: DescribeValidationErrors(response));
                    return (false, string.IsNullOrWhiteSpace(message) ? "Unable to save onboarding state." : message);
                }

                var verifyResult = await VerifyOnboardingPersistenceAsync(tenantEmail, isCompleted, operationName);
                if (!verifyResult.Success)
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: verifyResult.Inconclusive ? "SaveVerificationInconclusive" : "SaveVerificationFailed",
                        httpStatus: null,
                        businessStatus: null,
                        responseKeys: null,
                        hasConfigurationJson: null,
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null);

                    _logger?.LogWarning("Onboarding save verification failed. Action={Action}, Tenant={Tenant}, Reason={Reason}, Inconclusive={Inconclusive}", operationName, RedactTenant(tenantEmail), verifyResult.ErrorMessage, verifyResult.Inconclusive);

                    // Save call reported success, but read-back verification was inconclusive due
                    // downstream API issues. Do not treat this as persistence failure.
                    if (verifyResult.Inconclusive)
                    {
                        return (true, string.Empty);
                    }

                    return (false, verifyResult.ErrorMessage);
                }

                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "SaveAndVerifySucceeded",
                    httpStatus: "200",
                    businessStatus: "200",
                    responseKeys: null,
                    hasConfigurationJson: true,
                    hasDismissedTimestamp: true,
                    hasCompletedTimestamp: isCompleted ? true : false);
                return (true, string.Empty);
            }
            catch (ApiUnavailableException ex)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "SaveApiUnavailableException",
                    httpStatus: ex.StatusCode?.ToString(CultureInfo.InvariantCulture),
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null,
                    exception: ex);
                _logger?.LogError(ex, "Onboarding save request failed. Action={Action}, Tenant={Tenant}, HttpStatusCode={HttpStatusCode}, ResponseBodyPresent={ResponseBodyPresent}", operationName, RedactTenant(tenantEmail), ex.StatusCode, !string.IsNullOrWhiteSpace(ex.ResponseContent));
                return (false, "Unable to reach the onboarding service. Please try again.");
            }
            catch (Exception ex)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "SaveException",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null,
                    exception: ex);
                _logger?.LogError(ex, "Onboarding save request failed. Action={Action}, Tenant={Tenant}", operationName, RedactTenant(tenantEmail));
                return (false, "Unable to reach the onboarding service. Please try again.");
            }
        }

        private async Task<(bool Success, string ErrorMessage, bool Inconclusive)> VerifyOnboardingPersistenceAsync(string tenantEmail, bool isCompleted, string operationName)
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
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyNullResponse",
                        httpStatus: null,
                        businessStatus: null,
                        responseKeys: "null",
                        hasConfigurationJson: false,
                        hasDismissedTimestamp: false,
                        hasCompletedTimestamp: false);
                    _logger?.LogWarning("Onboarding verification returned null response. Action={Action}, Tenant={Tenant}", operationName, RedactTenant(tenantEmail));
                    return (false, "Unable to confirm onboarding save. Please try again.", true);
                }

                var verifyStatus = ParseApiStatus(persisted);
                _logger?.LogInformation(
                    "Onboarding verification response. Action={Action}, Tenant={Tenant}, StatusCode={StatusCode}, Shape={Shape}",
                    operationName,
                    RedactTenant(tenantEmail),
                    verifyStatus.StatusCode ?? "<missing>",
                    DescribeResponseShape(persisted));

                var cfgJson = ExtractConfigurationJson(persisted, out var payloadSource);

                if (string.IsNullOrWhiteSpace(cfgJson))
                {
                    var wrapperHttpStatus = NormalizeStatusCode(GetTokenString(persisted, "StatusCode"));
                    var businessStatus = NormalizeStatusCode(GetTokenString(payloadSource ?? GetResultSetObject(persisted), "StatusCode"));
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyReadBackMissingConfigurationJson",
                        httpStatus: wrapperHttpStatus,
                        businessStatus: businessStatus ?? verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: false,
                        hasDismissedTimestamp: false,
                        hasCompletedTimestamp: false,
                        validationErrors: DescribeValidationErrors(persisted));
                    _logger?.LogWarning(
                        "Onboarding verification failed. Stage={Stage}, Action={Action}, Tenant={Tenant}, HttpStatus={HttpStatus}, BusinessStatus={BusinessStatus}, WrapperKeys={WrapperKeys}, ExpectedDismissedTimestampExists={ExpectedDismissedTimestampExists}, ExceptionType={ExceptionType}",
                        "VerifyReadBackMissingConfigurationJson",
                        operationName,
                        RedactTenant(tenantEmail),
                        wrapperHttpStatus ?? "<missing>",
                        businessStatus ?? verifyStatus.StatusCode ?? "<missing>",
                        DescribeResponseShape(persisted),
                        false,
                        "<none>");
                    _logger?.LogWarning("Onboarding verification missing ConfigurationJson. Action={Action}, Tenant={Tenant}", operationName, RedactTenant(tenantEmail));
                    return (false, "Unable to confirm onboarding save. Please try again.", false);
                }

                var responseTenant = GetTokenString(payloadSource, "Email")
                    ?? GetTokenString(payloadSource, "TenantEmail")
                    ?? GetTokenString(payloadSource, "TenantName");
                if (!string.IsNullOrWhiteSpace(responseTenant)
                    && !string.Equals(responseTenant.Trim(), tenantEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyReadBackTenantMismatch",
                        httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                        businessStatus: verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: true,
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null);
                    _logger?.LogWarning(
                        "Onboarding verification failed. Stage={Stage}, Action={Action}, Tenant={Tenant}, WrapperKeys={WrapperKeys}",
                        "VerifyReadBackTenantMismatch",
                        operationName,
                        RedactTenant(tenantEmail),
                        DescribeResponseShape(persisted));
                    return (false, "Unable to confirm onboarding save. Please try again.", false);
                }

                var j = JObject.Parse(cfgJson);
                var configTenant = GetTokenString(j, "TenantName") ?? GetTokenString(j, "TenantEmail");
                if (!string.IsNullOrWhiteSpace(configTenant)
                    && !string.Equals(configTenant.Trim(), tenantEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyReadBackConfigurationTenantMismatch",
                        httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                        businessStatus: verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: true,
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null);
                    _logger?.LogWarning(
                        "Onboarding verification failed. Stage={Stage}, Action={Action}, Tenant={Tenant}, WrapperKeys={WrapperKeys}",
                        "VerifyReadBackConfigurationTenantMismatch",
                        operationName,
                        RedactTenant(tenantEmail),
                        DescribeResponseShape(persisted));
                    return (false, "Unable to confirm onboarding save. Please try again.", false);
                }

                var configStoreCode = GetTokenString(j, "StoreCode");
                var responseStoreCodesToken = GetToken(payloadSource, "Storecodes");
                var responseHasTenantStoreCode = ContainsTenantLevelStoreCode(responseStoreCodesToken);
                var configHasTenantStoreCode = string.IsNullOrWhiteSpace(configStoreCode);

                if (!responseHasTenantStoreCode && !configHasTenantStoreCode)
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyReadBackStoreCodeMismatch",
                        httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                        businessStatus: verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: true,
                        hasDismissedTimestamp: null,
                        hasCompletedTimestamp: null);
                    _logger?.LogWarning(
                        "Onboarding verification failed. Stage={Stage}, Action={Action}, Tenant={Tenant}, WrapperKeys={WrapperKeys}, ResponseHasTenantStoreCode={ResponseHasTenantStoreCode}, ConfigHasTenantStoreCode={ConfigHasTenantStoreCode}",
                        "VerifyReadBackStoreCodeMismatch",
                        operationName,
                        RedactTenant(tenantEmail),
                        DescribeResponseShape(persisted),
                        responseHasTenantStoreCode,
                        configHasTenantStoreCode);
                    return (false, "Unable to confirm onboarding save. Please try again.", false);
                }

                var dismissedUtc = GetTokenString(j, "OnboardingDismissedUtc");
                var completedUtc = GetTokenString(j, "OnboardingCompletedUtc");

                var hasDismissed = !string.IsNullOrWhiteSpace(dismissedUtc);
                var hasCompleted = !string.IsNullOrWhiteSpace(completedUtc);

                var dismissedParsed = DateTime.TryParse(dismissedUtc, null, DateTimeStyles.RoundtripKind, out _);
                var completedParsed = DateTime.TryParse(completedUtc, null, DateTimeStyles.RoundtripKind, out _);

                _logger?.LogInformation(
                    "Onboarding verification flags. Action={Action}, Tenant={Tenant}, HasDismissed={HasDismissed}, DismissedParsed={DismissedParsed}, HasCompleted={HasCompleted}, CompletedParsed={CompletedParsed}, IsCompletedRequest={IsCompletedRequest}",
                    operationName,
                    RedactTenant(tenantEmail),
                    hasDismissed,
                    dismissedParsed,
                    hasCompleted,
                    completedParsed,
                    isCompleted);

                if (!hasDismissed)
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyMissingDismissedTimestamp",
                        httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                        businessStatus: verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: true,
                        hasDismissedTimestamp: hasDismissed,
                        hasCompletedTimestamp: hasCompleted);
                    return (false, "Your onboarding preference could not be saved. Please try again.", false);
                }

                if (isCompleted && !hasCompleted)
                {
                    await WriteOnboardingDiagnosticsAsync(
                        operation: operationName,
                        stage: "VerifyMissingCompletedTimestamp",
                        httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                        businessStatus: verifyStatus.StatusCode,
                        responseKeys: DescribeResponseShape(persisted),
                        hasConfigurationJson: true,
                        hasDismissedTimestamp: hasDismissed,
                        hasCompletedTimestamp: hasCompleted);
                    return (false, "Your onboarding completion could not be saved. Please try again.", false);
                }

                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "VerifySucceeded",
                    httpStatus: NormalizeStatusCode(GetTokenString(persisted, "StatusCode")),
                    businessStatus: verifyStatus.StatusCode,
                    responseKeys: DescribeResponseShape(persisted),
                    hasConfigurationJson: true,
                    hasDismissedTimestamp: hasDismissed,
                    hasCompletedTimestamp: hasCompleted);

                return (true, string.Empty, false);
            }
            catch (ApiUnavailableException ex)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "VerifyApiUnavailableException",
                    httpStatus: ex.StatusCode?.ToString(CultureInfo.InvariantCulture),
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null,
                    exception: ex);
                _logger?.LogWarning(ex, "Onboarding verification API unavailable. Action={Action}, Tenant={Tenant}, HttpStatusCode={HttpStatusCode}, ResponseBodyPresent={ResponseBodyPresent}", operationName, RedactTenant(tenantEmail), ex.StatusCode, !string.IsNullOrWhiteSpace(ex.ResponseContent));
                return (false, "Unable to confirm onboarding save. Please try again.", true);
            }
            catch (Exception ex)
            {
                await WriteOnboardingDiagnosticsAsync(
                    operation: operationName,
                    stage: "VerifyException",
                    httpStatus: null,
                    businessStatus: null,
                    responseKeys: null,
                    hasConfigurationJson: null,
                    hasDismissedTimestamp: null,
                    hasCompletedTimestamp: null,
                    exception: ex);
                _logger?.LogWarning(ex, "Onboarding save verification request failed. Action={Action}, Tenant={Tenant}", operationName, RedactTenant(tenantEmail));
                return (false, "Unable to confirm onboarding save. Please try again.", true);
            }
        }

        private async Task WriteOnboardingDiagnosticsAsync(
            string operation,
            string stage,
            string httpStatus,
            string businessStatus,
            string responseKeys,
            bool? hasConfigurationJson,
            bool? hasDismissedTimestamp,
            bool? hasCompletedTimestamp,
            Exception exception = null,
            string validationErrors = null)
        {
            var line = string.Join(" | ", new[]
            {
                $"TimestampUtc={DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}",
                $"Operation={operation ?? "<unknown>"}",
                $"Stage={stage ?? "<unknown>"}",
                $"HttpStatus={httpStatus ?? "<n/a>"}",
                $"BusinessStatus={businessStatus ?? "<n/a>"}",
                $"ResponseKeys={responseKeys ?? "<n/a>"}",
                $"HasConfigurationJson={FormatBool(hasConfigurationJson)}",
                $"HasDismissedTimestamp={FormatBool(hasDismissedTimestamp)}",
                $"HasCompletedTimestamp={FormatBool(hasCompletedTimestamp)}",
                $"ValidationErrors={validationErrors ?? "<none>"}",
                $"ExceptionType={exception?.GetType().Name ?? "<none>"}",
                $"ExceptionMessage={exception?.Message ?? "<none>"}"
            });

            try
            {
                var directory = Path.GetDirectoryName(_onboardingDiagnosticsLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await OnboardingDiagnosticsFileLock.WaitAsync();
                await System.IO.File.AppendAllTextAsync(_onboardingDiagnosticsLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to write onboarding diagnostics file entry.");
            }
            finally
            {
                if (OnboardingDiagnosticsFileLock.CurrentCount == 0)
                {
                    OnboardingDiagnosticsFileLock.Release();
                }
            }
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : "<n/a>";
        }

        private static string DescribeValidationErrors(JObject response)
        {
            if (response == null)
            {
                return null;
            }

            var title = GetTokenString(response, "title");
            var status = GetTokenString(response, "status");
            var errorsToken = GetToken(response, "errors");
            if (errorsToken is not JObject errorsObject)
            {
                return null;
            }

            var keys = string.Join(",", errorsObject.Properties().Select(p => p.Name));
            return $"title={title ?? "<none>"};status={status ?? "<none>"};keys=[{keys}]";
        }

        private static string ExtractConfigurationJson(JObject response, out JObject sourceObject)
        {
            sourceObject = null;
            if (response == null)
            {
                return null;
            }

            foreach (var candidate in EnumerateObjects(response))
            {
                var cfgJson = GetTokenString(candidate, "ConfigurationJson");
                if (!string.IsNullOrWhiteSpace(cfgJson))
                {
                    sourceObject = candidate;
                    return cfgJson;
                }
            }

            return null;
        }

        private static IEnumerable<JObject> EnumerateObjects(JToken token)
        {
            if (token is JObject obj)
            {
                yield return obj;

                foreach (var property in obj.Properties())
                {
                    foreach (var nested in EnumerateObjects(property.Value))
                    {
                        yield return nested;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var nested in EnumerateObjects(item))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool ContainsTenantLevelStoreCode(JToken storeCodesToken)
        {
            if (storeCodesToken == null || storeCodesToken.Type == JTokenType.Null)
            {
                return false;
            }

            if (storeCodesToken is JArray array)
            {
                return array.Any(item => string.IsNullOrWhiteSpace(item?.ToString()));
            }

            return string.IsNullOrWhiteSpace(storeCodesToken.ToString());
        }

        private static (string StatusCode, string Message) ParseApiStatus(JObject response)
        {
            var resultSet = GetResultSetObject(response);
            var resultSetStatus = NormalizeStatusCode(GetTokenString(resultSet, "StatusCode"));
            var resultSetMessage = GetTokenString(resultSet, "Message");
            if (!string.IsNullOrWhiteSpace(resultSetStatus) || !string.IsNullOrWhiteSpace(resultSetMessage))
            {
                return (resultSetStatus, resultSetMessage);
            }

            var rootStatus = GetTokenString(response, "StatusCode");
            var rootMessage = GetTokenString(response, "Message");
            var normalizedRootStatus = NormalizeStatusCode(rootStatus);

            if (!string.IsNullOrWhiteSpace(normalizedRootStatus) || !string.IsNullOrWhiteSpace(rootMessage))
            {
                return (normalizedRootStatus, rootMessage);
            }

            return (null, null);
        }

        private static string NormalizeStatusCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (int.TryParse(trimmed, out var numeric))
            {
                return numeric.ToString(CultureInfo.InvariantCulture);
            }

            if (Enum.TryParse<HttpStatusCode>(trimmed, true, out var httpStatus))
            {
                return ((int)httpStatus).ToString(CultureInfo.InvariantCulture);
            }

            return trimmed;
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
            var token = GetToken(source, name);
            return token?.ToString();
        }

        private static JToken GetToken(JObject source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var direct = source[name];
            if (direct != null)
            {
                return direct;
            }

            var match = source.Properties()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return match?.Value;
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

        private static string NormalizeSubscriptionStatus(string subscriptionStatus, DateTime? trialEndUtc)
        {
            if (!string.IsNullOrWhiteSpace(subscriptionStatus))
            {
                var normalized = subscriptionStatus.Trim();
                if (string.Equals(normalized, "TrialActive", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Trial", StringComparison.OrdinalIgnoreCase))
                {
                    if (trialEndUtc.HasValue && trialEndUtc.Value.ToUniversalTime() < DateTime.UtcNow)
                    {
                        return "Expired";
                    }

                    return "Trial";
                }

                if (string.Equals(normalized, "Active", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Suspended", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Expired", StringComparison.OrdinalIgnoreCase))
                {
                    return normalized;
                }

                return normalized;
            }

            if (trialEndUtc.HasValue)
            {
                return trialEndUtc.Value.ToUniversalTime() >= DateTime.UtcNow ? "Trial" : "Expired";
            }

            return "Not configured";
        }

        private static string ResolveRegisteredDomains(LoginIoResponse profile, string tenantEmail)
        {
            return "Not configured";
        }

        private static string ResolveBackupServiceStatus(LoginIoResponse profile)
        {
            return "Not configured";
        }

        private static DateTime? ResolveSubscriptionExpiryUtc(string rawSubscriptionStatus, string normalizedSubscriptionStatus, DateTime? trialEndUtc)
        {
            if (trialEndUtc.HasValue
                && (string.Equals(normalizedSubscriptionStatus, "Trial", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rawSubscriptionStatus, "Trial", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rawSubscriptionStatus, "TrialActive", StringComparison.OrdinalIgnoreCase)))
            {
                return trialEndUtc.Value.ToUniversalTime();
            }

            return null;
        }
    }
}