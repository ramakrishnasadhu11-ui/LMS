using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using MailKit.Security;
using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using LMSClientFactory.Helper;
using LMSWebUI.Data;
using LMSWebUI.Models;
using LMSWebUI.Models.Admin;
using LMSWebUI.Models.Customer;
using LMSWebUI.Models.Login;
using System.ComponentModel.DataAnnotations;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace LMSWebUI.Controllers
{
    [Authorize]
    public class CustomerController : Controller
    {
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _memoryCache;
        private readonly ICustomerApiClient _clientApi;
        private readonly ILoginApiClient _loginApi;
        private readonly IStoreConfigurationService _storeConfigurationService;
        private readonly LMSWebUI.Services.ICurrencyService _currencyService;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<CustomerController> _logger;
        private readonly ICustomerPhoneService _customerPhoneService;
        private const string CustomerPreferencesSessionKey = "CustomerPreferences";
        private const string SelectedCustomerNameSessionKey = "SelectedCustomerName";
        private const string SelectedCustomerCodeSessionKey = "SelectedCustomerCode";
        private const string AllowedStoresSessionKey = "TenantAllowedStores";
        private const string OrderPiecesMapSessionKey = "OrderPiecesMap";
        private const string WalkInCustomerInitializedStoresSessionKey = "WalkInCustomerInitializedStores";
        private const string CustomerNotInStoreMessage = "Customer not found in the current store.";
        private const string SessionExpiredMessage = "Session expired. Please log in again.";
        private const string DefaultWalkInCustomerName = "Walk-in Customer";
        private const long MaxCsvUploadBytes = 2 * 1024 * 1024;
        private static readonly Regex E164Regex = new Regex("^\\+[1-9]\\d{6,14}$", RegexOptions.Compiled);
        private static readonly Regex LegacyDigitsRegex = new Regex("^\\d{7,15}$", RegexOptions.Compiled);

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executedContext = await next();

            if (executedContext.Exception is UnauthorizedAccessException ex
                && !executedContext.ExceptionHandled)
            {
                var message = string.IsNullOrWhiteSpace(ex.Message)
                    ? SessionExpiredMessage
                    : ex.Message;

                var isAjaxRequest = string.Equals(
                        context.HttpContext?.Request?.Headers["X-Requested-With"].ToString(),
                        "XMLHttpRequest",
                        StringComparison.OrdinalIgnoreCase)
                    || (context.HttpContext?.Request?.Headers["Accept"].ToString() ?? string.Empty)
                        .Contains("application/json", StringComparison.OrdinalIgnoreCase);

                if (isAjaxRequest)
                {
                    executedContext.Result = new JsonResult(new
                    {
                        success = false,
                        message,
                        redirectUrl = Url.Action("Login", "Login")
                    })
                    {
                        StatusCode = StatusCodes.Status401Unauthorized
                    };
                }

                else
                {
                    TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                    {
                        CssClassName = "alert alert-danger",
                        Title = "Failed.",
                        DisplayMessage = message
                    });

                    executedContext.Result = RedirectToAction("Login", "Login");
                }

                executedContext.ExceptionHandled = true;
            }
        }

        /// <summary>
        /// Reads the logged-in store scope from the session. Customer lookups are restricted
        /// to this scope so customers belonging to other stores are never returned.
        /// </summary>
        private bool TryGetCustomerScope(out string tenantName, out string storeCode)
        {
            tenantName = HttpContext.Session.GetString("TenantName");
            storeCode = HttpContext.Session.GetString("TenantStore");

            return !string.IsNullOrWhiteSpace(tenantName) && !string.IsNullOrWhiteSpace(storeCode);
        }

        /// <summary>
        /// Builds a parameter dictionary already scoped to the logged-in store.
        /// </summary>
        private Dictionary<string, string> BuildScopedParameters(params (string Key, string Value)[] parameters)
        {
            TryGetCustomerScope(out var tenantName, out var storeCode);

            var scopedParameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName ?? string.Empty },
                { "storeCode", storeCode ?? string.Empty }
            };

            foreach (var (key, value) in parameters)
            {
                scopedParameters[key] = value ?? string.Empty;
            }

            return scopedParameters;
        }

        private bool IsAdminUser()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            return string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(userRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        }

        private string GetAuthenticatedTenantId()
        {
            var sessionTenantId = HttpContext.Session.GetString("TenantId");
            if (!string.IsNullOrWhiteSpace(sessionTenantId))
            {
                return sessionTenantId.Trim();
            }

            var claimTenantId = User.FindFirst("tenantId")?.Value;
            if (!string.IsNullOrWhiteSpace(claimTenantId))
            {
                return claimTenantId.Trim();
            }

            var accessToken = HttpContext.Session.GetString("AccessToken");
            return GetJwtClaimValue(accessToken, "tenantId", "tenant_id", "tid", "TenantId");
        }

        private static string GetJwtClaimValue(string jwtToken, params string[] claimNames)
        {
            if (string.IsNullOrWhiteSpace(jwtToken) || claimNames == null || claimNames.Length == 0)
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

                var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payloadSegment)));
                foreach (var claimName in claimNames)
                {
                    var claim = payload.Properties().FirstOrDefault(property =>
                        string.Equals(property.Name, claimName, StringComparison.OrdinalIgnoreCase));
                    if (claim != null && !string.IsNullOrWhiteSpace(claim.Value?.ToString()))
                    {
                        return claim.Value.ToString().Trim();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool IsSuperAdminUser()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            return string.Equals(userRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanManageStoreOrderWorkflow()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            return string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(userRole, "StoreUser", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildWalkInStoreInitializationKey(string tenantName, string storeCode)
            => $"{tenantName?.Trim().ToLowerInvariant()}|{storeCode?.Trim().ToLowerInvariant()}";

        private bool IsWalkInCustomerInitializedForStore(string tenantName, string storeCode)
        {
            var key = BuildWalkInStoreInitializationKey(tenantName, storeCode);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var raw = HttpContext.Session.GetString(WalkInCustomerInitializedStoresSessionKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var stores = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));

            return stores.Contains(key, StringComparer.Ordinal);
        }

        private void MarkWalkInCustomerInitializedForStore(string tenantName, string storeCode)
        {
            var key = BuildWalkInStoreInitializationKey(tenantName, storeCode);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var raw = HttpContext.Session.GetString(WalkInCustomerInitializedStoresSessionKey) ?? string.Empty;
            var stores = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);

            if (stores.Add(key))
            {
                HttpContext.Session.SetString(WalkInCustomerInitializedStoresSessionKey, string.Join(",", stores));
            }
        }

        private static bool IsScopedCustomerFound(CustomerIOResponse customer, string expectedName = null)
            => customer != null
               && string.Equals(customer.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(customer.CustCode)
               && (string.IsNullOrWhiteSpace(expectedName)
                   || string.Equals(customer.CustomerName?.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase));

        private async Task<CustomerIOResponse> ResolveScopedCustomerByNameAsync(string tenantName, string storeCode, string customerName)
        {
            var normalizedName = customerName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName)
                || string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            var scopedParams = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode },
                { "customerName", normalizedName }
            };

            var customer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", scopedParams, Method.GET);
            if (IsScopedCustomerFound(customer, normalizedName))
            {
                return customer;
            }

            var searchResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SearchCustomers", new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode },
                { "searchText", normalizedName }
            }, Method.GET);

            var names = searchResponse?.CustomerNames ?? new List<string>();
            var bestMatch = names.FirstOrDefault(x => string.Equals(x?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(bestMatch))
            {
                return customer;
            }

            return await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode },
                { "customerName", bestMatch.Trim() }
            }, Method.GET);
        }

        private async Task EnsureDefaultWalkInCustomerInitializedAsync(string tenantName, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || IsWalkInCustomerInitializedForStore(tenantName, storeCode))
            {
                return;
            }

            try
            {
                var existingCustomer = await ResolveScopedCustomerByNameAsync(tenantName, storeCode, DefaultWalkInCustomerName);
                if (IsScopedCustomerFound(existingCustomer, DefaultWalkInCustomerName))
                {
                    MarkWalkInCustomerInitializedForStore(tenantName, storeCode);
                    return;
                }

                var createRequest = new
                {
                    CustomerName = DefaultWalkInCustomerName,
                    Address = $"Walk-in ({storeCode.Trim()})",
                    BarCode = (string)null,
                    PhoneNumber = (string)null,
                    Email = (string)null,
                    StoreCode = storeCode,
                    TenantName = tenantName
                };

                var createResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/InsertCustomer", createRequest, Method.POST);
                if (string.Equals(createResponse?.StatusCode, "200", StringComparison.OrdinalIgnoreCase))
                {
                    MarkWalkInCustomerInitializedForStore(tenantName, storeCode);
                    return;
                }

                var postCreateLookup = await ResolveScopedCustomerByNameAsync(tenantName, storeCode, DefaultWalkInCustomerName);
                if (IsScopedCustomerFound(postCreateLookup, DefaultWalkInCustomerName))
                {
                    MarkWalkInCustomerInitializedForStore(tenantName, storeCode);
                }
            }
            catch
            {
                // Best effort onboarding bootstrap. Order screen remains available even if initialization cannot complete.
            }
        }

        private static bool ShouldFallbackApiEndpoint(Exception ex)
        {
            var message = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
            return ex is ApiUnavailableException
                || message.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                || message.Contains("404", StringComparison.OrdinalIgnoreCase)
                || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
                || message.Contains("A root ambient transaction was completed before the nested transaction", StringComparison.OrdinalIgnoreCase)
                || message.Contains("nested transaction", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<LoginIoResponse> TryCreateTenantStoreAsync(string tenantName, string newStoreCode)
        {
            var endpointCandidates = new[] { "/CreateTenantStore", "/CreateStore", "/CreateNewStore" };
            var createRequest = new
            {
                TenantName = tenantName,
                StoreCode = newStoreCode
            };

            var createRequestDictionary = new Dictionary<string, string>
            {
                { "TenantName", tenantName ?? string.Empty },
                { "tenantName", tenantName ?? string.Empty },
                { "StoreCode", newStoreCode ?? string.Empty },
                { "storeCode", newStoreCode ?? string.Empty }
            };

            Exception lastException = null;

            foreach (var endpoint in endpointCandidates)
            {
                try
                {
                    var response = await _loginApi.SendRequestAsync<LoginIoResponse>(endpoint, createRequest, Method.POST);
                    if (response != null)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (!ShouldFallbackApiEndpoint(ex))
                    {
                        throw;
                    }
                }

                try
                {
                    var response = await _loginApi.SendRequestAsync<LoginIoResponse>(endpoint, createRequestDictionary, Method.POST);
                    if (response != null)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (!ShouldFallbackApiEndpoint(ex))
                    {
                        throw;
                    }
                }

                try
                {
                    var response = await _clientApi.SendRequestAsync<LoginIoResponse>(endpoint, createRequest, Method.POST);
                    if (response != null)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (!ShouldFallbackApiEndpoint(ex))
                    {
                        throw;
                    }
                }

                try
                {
                    var response = await _clientApi.SendRequestAsync<LoginIoResponse>(endpoint, createRequestDictionary, Method.POST);
                    if (response != null)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (!ShouldFallbackApiEndpoint(ex))
                    {
                        throw;
                    }
                }
            }

            if (lastException != null)
            {
                throw lastException;
            }

            return null;
        }

        private List<string> GetAllowedStores(string currentStoreCode)
        {
            var allowedStores = (HttpContext.Session.GetString(AllowedStoresSessionKey) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(currentStoreCode)
                && !allowedStores.Contains(currentStoreCode, StringComparer.OrdinalIgnoreCase))
            {
                allowedStores.Add(currentStoreCode);
            }

            return allowedStores;
        }

        private bool TryResolveTargetStore(string currentStoreCode, string requestedStoreCode, out string resolvedStoreCode, out string validationMessage)
        {
            resolvedStoreCode = string.IsNullOrWhiteSpace(requestedStoreCode)
                ? currentStoreCode
                : requestedStoreCode.Trim();

            if (string.IsNullOrWhiteSpace(resolvedStoreCode))
            {
                validationMessage = "Session expired. Please log in again.";
                return false;
            }

            var allowedStores = GetAllowedStores(currentStoreCode);
            if (allowedStores.Count > 0 && !allowedStores.Contains(resolvedStoreCode, StringComparer.OrdinalIgnoreCase))
            {
                validationMessage = "Invalid store selected.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private async Task<(bool IsValid, string Message)> ValidateCurrentStoreAccessAsync(
            string tenantName,
            string currentStoreCode,
            bool requireOrderPermission = false,
            bool requireSubscriptionEligibility = false)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return (false, SessionExpiredMessage);
            }

            if (!TryResolveTargetStore(currentStoreCode, currentStoreCode, out var resolvedStoreCode, out var validationMessage))
            {
                return (false, string.IsNullOrWhiteSpace(validationMessage) ? "Invalid store selected." : validationMessage);
            }

            var storeValidation = await ValidateTenantActiveStoreSelectionAsync(tenantName, currentStoreCode, resolvedStoreCode);
            if (!storeValidation.IsValid)
            {
                return (false, string.IsNullOrWhiteSpace(storeValidation.Message)
                    ? "Selected store is invalid or inactive. Please select an active store."
                    : storeValidation.Message);
            }

            if (requireOrderPermission && !CanManageStoreOrderWorkflow())
            {
                return (false, "You do not have permission to create orders for this store.");
            }

            if (requireSubscriptionEligibility)
            {
                var subscriptionValidation = await ValidateSubscriptionEligibilityForOrderAsync();
                if (!subscriptionValidation.IsValid)
                {
                    return subscriptionValidation;
                }
            }

            return (true, string.Empty);
        }

        private async Task<(bool IsValid, string Message)> ValidateSubscriptionEligibilityForOrderAsync()
        {
            try
            {
                var overview = await _loginApi.SendRequestAsync<JObject>("/GetTenantSubscriptionOverview", new Dictionary<string, string>(), Method.GET);

                var status = (overview?["SubscriptionStatus"]
                    ?? overview?["subscriptionStatus"]
                    ?? overview?["AccountStatus"]
                    ?? overview?["accountStatus"])?.ToString()?.Trim();

                var trialEndUtc = TryParseDateTime(overview?["TrialEndUtc"] ?? overview?["trialEndUtc"]);

                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, "Your subscription is not active for creating orders. Please renew or contact support.");
                    }

                    if ((string.Equals(status, "Trial", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "TrialActive", StringComparison.OrdinalIgnoreCase))
                        && trialEndUtc.HasValue
                        && trialEndUtc.Value.ToUniversalTime() < DateTime.UtcNow)
                    {
                        return (false, "Your trial period has expired. Please activate a subscription to create orders.");
                    }
                }

                return (true, string.Empty);
            }
            catch
            {
                return (false, "Unable to validate subscription eligibility for order creation. Please try again.");
            }
        }

        private Dictionary<string, int> GetOrderPiecesMap()
        {
            var payload = HttpContext.Session.GetString(OrderPiecesMapSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, int>>(payload)
                    ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveOrderPieces(string orderNo, int totalPieces)
        {
            if (string.IsNullOrWhiteSpace(orderNo) || totalPieces <= 0)
            {
                return;
            }

            var map = GetOrderPiecesMap();
            map[orderNo.Trim()] = totalPieces;
            HttpContext.Session.SetString(OrderPiecesMapSessionKey, JsonConvert.SerializeObject(map));
        }

        private bool TryGetOrderPieces(string orderNo, out int totalPieces)
        {
            totalPieces = 0;
            if (string.IsNullOrWhiteSpace(orderNo))
            {
                return false;
            }

            var map = GetOrderPiecesMap();
            return map.TryGetValue(orderNo.Trim(), out totalPieces) && totalPieces > 0;
        }

        private static IEnumerable<string> ExtractStoreUserEmails(JObject response)
        {
            var usersToken = response?["Users"]
                             ?? response?["users"]
                             ?? response?["ResultSet"]?["Users"]
                             ?? response?["ResultSet"]?["users"]
                             ?? response?["resultSet"]?["Users"]
                             ?? response?["resultSet"]?["users"]
                             ?? response?["$values"]
                             ?? new JArray();

            var usersArray = usersToken as JArray ?? usersToken?["$values"] as JArray ?? new JArray();

            return usersArray
                .Select(x => (x?["Email"]?.ToString() ?? x?["email"]?.ToString())?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string NormalizeEmail(string email)
            => (email ?? string.Empty).Trim().ToLowerInvariant();

        private async Task<bool> IsStoreUserEmailExistsAcrossApplicationAsync(string normalizedEmail, string tenantEmail, List<string> storesToValidate)
        {
            async Task<IEnumerable<string>> getUsersByScopeAsync(string scopeTenantEmail, string scopeStoreCode)
            {
                try
                {
                    var scopeResponse = await _loginApi.SendRequestAsync<JObject>("/GetStoreUsers", new Dictionary<string, string>
                    {
                        { "tenantEmail", scopeTenantEmail },
                        { "storeCode", scopeStoreCode }
                    }, Method.GET);

                    return ExtractStoreUserEmails(scopeResponse).Select(NormalizeEmail);
                }
                catch
                {
                    return Enumerable.Empty<string>();
                }
            }

            var globalUsers = await getUsersByScopeAsync(string.Empty, string.Empty);
            if (globalUsers.Any(x => x == normalizedEmail))
            {
                return true;
            }

            var tenantUsers = await getUsersByScopeAsync(tenantEmail, string.Empty);
            if (tenantUsers.Any(x => x == normalizedEmail))
            {
                return true;
            }

            foreach (var validateStoreCode in storesToValidate)
            {
                var usersByStore = await getUsersByScopeAsync(tenantEmail, validateStoreCode);
                if (usersByStore.Any(x => x == normalizedEmail))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<List<string>> GetTenantStoreCodesForValidationAsync(string tenantEmail, string currentStoreCode)
        {
            var storeCodes = new List<string>();
            if (!string.IsNullOrWhiteSpace(tenantEmail))
            {
                var parms = new Dictionary<string, string>
                {
                    { "eMail", tenantEmail }
                };

                try
                {
                    var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", parms, Method.GET);
                    if (response?.Storecodes != null)
                    {
                        storeCodes.AddRange(response.Storecodes);
                    }
                }
                catch
                {
                    try
                    {
                        var fallbackResponse = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.GET);
                        if (fallbackResponse?.Storecodes != null)
                        {
                            storeCodes.AddRange(fallbackResponse.Storecodes);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentStoreCode))
            {
                storeCodes.Add(currentStoreCode);
            }

            return storeCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<(bool IsValid, string Message)> ValidateTenantActiveStoreSelectionAsync(string tenantEmail, string currentStoreCode, string selectedStoreCode)
        {
            if (string.IsNullOrWhiteSpace(selectedStoreCode))
            {
                return (false, "Please select an active store.");
            }

            var normalizedStore = selectedStoreCode.Trim();
            var (tenantStores, loadErrorMessage) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantEmail, currentStoreCode);
            var matchingStore = tenantStores.FirstOrDefault(x => x != null
                && !string.IsNullOrWhiteSpace(x.StoreCode)
                && string.Equals(x.StoreCode, normalizedStore, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(x.TenantName, tenantEmail, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.TenantEmail, tenantEmail, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(x.TenantName) && string.IsNullOrWhiteSpace(x.TenantEmail)));

            if (matchingStore == null)
            {
                if (!string.IsNullOrWhiteSpace(loadErrorMessage))
                {
                    return (false, loadErrorMessage);
                }

                return (false, "Selected store does not belong to your tenant.");
            }

            if (!matchingStore.IsActive)
            {
                return (false, "Selected store is inactive. Please select an active store.");
            }

            return (true, string.Empty);
        }

        private async Task<List<string>> GetActiveTenantStoreCodesAsync(string tenantEmail, string currentStoreCode)
        {
            var (tenantStores, _) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantEmail, currentStoreCode);
            return tenantStores
                .Where(x => x != null
                    && x.IsActive
                    && !string.IsNullOrWhiteSpace(x.StoreCode)
                    && (string.Equals(x.TenantName, tenantEmail, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.TenantEmail, tenantEmail, StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(x.TenantName) && string.IsNullOrWhiteSpace(x.TenantEmail)))
                .Select(x => x.StoreCode.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IActionResult AdminOnlyView(string featureTitle, string description)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access this page."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = new AdminFeatureViewModel
            {
                FeatureTitle = featureTitle,
                Description = description,
                TenantName = tenantName,
                StoreCode = storeCode
            };

            return View("AdminFeature", model);
        }

        private void SetCreateModeState()
        {
            TempData["isHideButton"] = "false";
            TempData["customerCode"] = string.Empty;
        }

        public CustomerController(
            Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache,
            ICustomerApiClient clientAPI,
            ILoginApiClient loginApi,
            IStoreConfigurationService storeConfigurationService,
            LMSWebUI.Services.ICurrencyService currencyService,
            IConfiguration configuration,
            ApplicationDbContext dbContext,
            ILogger<CustomerController> logger,
            ICustomerPhoneService customerPhoneService = null)
        {
            _memoryCache = memoryCache;
            _clientApi = clientAPI;
            _loginApi = loginApi;
            _storeConfigurationService = storeConfigurationService;
            _currencyService = currencyService;
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
            _customerPhoneService = customerPhoneService ?? new CustomerPhoneService();
        }

        public async Task<IActionResult> Index(string customerName = null, string mode = null)
        {
            var customerInfoDto = new CustomerInfoDto
            {
                disableStatus = false
            };

            var actionMode = string.IsNullOrWhiteSpace(mode)
                ? "create"
                : mode.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (mode.Equals("create", StringComparison.OrdinalIgnoreCase))
                {
                    SetCreateModeState();
                }
                else if (mode.Equals("update", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["isHideButton"] = "true";

                    if (string.IsNullOrWhiteSpace(customerName))
                    {
                        TempData["UpdateModeToast"] = "Update mode enabled. Search and select a customer to edit.";
                    }
                }
                else if (mode.Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["isHideButton"] = "true";

                    if (string.IsNullOrWhiteSpace(customerName))
                    {
                        TempData["UpdateModeToast"] = "Delete mode enabled. Search and select a customer to remove.";
                    }
                }
            }

            ViewData["CustomerActionMode"] = actionMode;

            if (!string.IsNullOrWhiteSpace(customerName))
            {
                var normalizedCustomerName = customerName.Trim();
                ViewData["UpdateSearchTerm"] = normalizedCustomerName;

                var parameters = BuildScopedParameters(("customerName", normalizedCustomerName));

                try
                {
                    var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", parameters, Method.GET);
                    if (responseMessage != null && responseMessage.StatusCode == "200")
                    {
                        customerInfoDto.CustomerName = responseMessage.CustomerName;
                        customerInfoDto.Address = responseMessage.Address;
                        // MembershipId removed system-wide
                        customerInfoDto.BarCode = responseMessage.BarCode;
                        customerInfoDto.PhoneCountryCode = _customerPhoneService.GetRegionForNumber(responseMessage.PhoneNumber);
                        customerInfoDto.PhoneNumber = _customerPhoneService.GetLocalNumber(
                            responseMessage.PhoneNumber,
                            customerInfoDto.PhoneCountryCode);
                        customerInfoDto.Email = responseMessage.Email;
                        customerInfoDto.customerCode = responseMessage.CustCode;
                        customerInfoDto.disableStatus = true;

                        TempData["isHideButton"] = "true";
                        TempData["customerCode"] = responseMessage.CustCode;

                        HttpContext.Session.SetString(SelectedCustomerNameSessionKey, responseMessage.CustomerName ?? normalizedCustomerName);
                        HttpContext.Session.SetString(SelectedCustomerCodeSessionKey, responseMessage.CustCode ?? string.Empty);
                    }
                    else
                    {
                        TempData["UpdateSearchToast"] = CustomerNotInStoreMessage;
                    }
                }
                catch
                {
                    TempData["UpdateSearchToast"] = CustomerNotInStoreMessage;
                }
            }

            // Load customer preferences for the logged-in store so the Create UI can adapt
            try
            {
                var prefParams = BuildScopedParameters(("tenantName", HttpContext.Session.GetString("TenantName")), ("storeCode", HttpContext.Session.GetString("TenantStore")));
                var prefs = await _clientApi.SendRequestAsync<CustomerPreferencesDto>("/GetCustomerPreferences", prefParams, Method.GET);
                ViewData["CustomerPreferences"] = JsonConvert.SerializeObject(prefs ?? BuildDefaultCustomerPreferences(HttpContext.Session.GetString("TenantName"), HttpContext.Session.GetString("TenantStore")));
            }
            catch
            {
                ViewData["CustomerPreferences"] = JsonConvert.SerializeObject(BuildDefaultCustomerPreferences(HttpContext.Session.GetString("TenantName"), HttpContext.Session.GetString("TenantStore")));
            }

            await LoadCustomerPhoneContextAsync(customerInfoDto);

            return View(customerInfoDto);
        }

        private async Task LoadCustomerPhoneContextAsync(CustomerInfoDto customerInfoDto)
        {
            var countries = _customerPhoneService.GetSupportedCountries();
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            var defaultCountry = _customerPhoneService.FindCountry(customerInfoDto?.PhoneCountryCode);

            try
            {
                var storeConfig = await _storeConfigurationService.LoadAsync(tenantName, storeCode);
                defaultCountry ??= _customerPhoneService.FindCountryByName(storeConfig?.Country);
                defaultCountry ??= countries.FirstOrDefault(country =>
                    string.Equals(country.CallingCode, storeConfig?.PhoneDialingCode?.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // The registration form remains usable when an optional store configuration is unavailable.
            }

            if (defaultCountry == null)
            {
                try
                {
                    var tenantCountry = await GetTenantCountryCurrencyAsync(tenantName);
                    defaultCountry = _customerPhoneService.FindCountry(tenantCountry.CountryCode);
                }
                catch
                {
                    // Missing tenant profile country is handled as an optional configuration warning below.
                }
            }

            customerInfoDto.PhoneCountryCode ??= defaultCountry?.Iso2;
            ViewData["CustomerPhoneCountries"] = JsonConvert.SerializeObject(countries);
            ViewData["CustomerDefaultPhoneCountry"] = defaultCountry?.Iso2 ?? string.Empty;
            ViewData["CustomerPhoneConfigMessage"] = defaultCountry == null
                ? "Tenant country is not configured. Select the customer’s country to validate the number."
                : string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> AddNewCustomer()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            var scopeValidation = await ValidateCurrentStoreAccessAsync(tenantName, storeCode);
            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Login", "Login");
            }

            SetCreateModeState();
            return View("Index", new CustomerInfoDto
            {
                disableStatus = false
            });
        }

        [HttpGet]
        public async Task<IActionResult> CustomerPreferences()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            CustomerPreferencesDto model;

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerPreferencesDto>("/GetCustomerPreferences", parameters, Method.GET);

                if (response != null)
                {
                    model = response;
                }
                else
                {
                    model = BuildDefaultCustomerPreferences(tenantName, storeCode);
                }
            }
            catch
            {
                model = BuildDefaultCustomerPreferences(tenantName, storeCode);
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> StoreTimings()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access store configuration."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(tenantName, storeCode);
            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Login", "Login");
            }

            var model = await _storeConfigurationService.LoadAsync(tenantName, storeCode);

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.IsExistingRecord = model.IsExistingRecord || _storeConfigurationService.HasData(model);

            try
            {
                var parms = new Dictionary<string, string> { { "eMail", tenantName } };
                var profile = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.GET);
                if (profile != null)
                {
                    model.Country = profile.Country;
                    model.CurrencyDisplay = string.Join(" ", new[] { profile.CurrencyCode, profile.CurrencySymbol }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                    model.TimeZoneIana = string.IsNullOrWhiteSpace(profile.TimeZoneIana) ? model.TimeZoneIana : profile.TimeZoneIana;
                    model.PhoneDialingCode = string.IsNullOrWhiteSpace(profile.PhoneDialingCode) ? model.PhoneDialingCode : profile.PhoneDialingCode;
                }
            }
            catch
            {
                // keep store configuration fallback values
            }

            if (!string.IsNullOrWhiteSpace(model.LoadErrorMessage) && TempData["UserMessage"] == null)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-danger",
                    Title = "Failed.",
                    DisplayMessage = model.LoadErrorMessage
                });
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreTimings(StoreConfigurationViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access store configuration."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            model = _storeConfigurationService.Normalize(model);
            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            var existingConfig = await _storeConfigurationService.LoadAsync(tenantName, storeCode);
            model.IsExistingRecord = _storeConfigurationService.HasData(existingConfig);
            model.Country = existingConfig?.Country;
            model.CurrencyDisplay = existingConfig?.CurrencyDisplay;
            model.TimeZoneIana = model.TimeZoneIana?.Trim();
            model.PhoneDialingCode = model.PhoneDialingCode?.Trim();
            model.PhoneNumber = NormalizePhoneNumberForStorage(model.PhoneNumber);

            if (!ModelState.IsValid)
            {
                var modelErrors = ModelState.Values
                    .SelectMany(x => x.Errors)
                    .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? x.Exception?.Message : x.ErrorMessage)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-danger",
                    Title = "Validation Failed!",
                    DisplayMessage = modelErrors.Count == 0
                        ? "Please provide valid store configuration values"
                        : string.Join(" ", modelErrors)
                });

                return View(model);
            }

            if (!ValidateStoreConfiguration(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            try
            {
                var (saveSuccess, saveMessage) = await _storeConfigurationService.SaveAsync(model);
                if (!saveSuccess)
                {
                    TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                    {
                        CssClassName = "alert-danger",
                        Title = "Failed.",
                        DisplayMessage = string.IsNullOrWhiteSpace(saveMessage)
                            ? "Unable to save store configuration"
                            : saveMessage
                    });

                    return View(model);
                }

                var userRole = HttpContext.Session.GetString("UserRole") ?? string.Empty;
                if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(userRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    var localizationPayload = new
                    {
                        TimeZoneIana = model.TimeZoneIana,
                        PhoneDialingCode = model.PhoneDialingCode
                    };

                    try
                    {
                        await _loginApi.SendRequestAsync<LoginIoResponse>(
                            $"/UpdateTenantLocalizationSettings?tenantEmail={Uri.EscapeDataString(tenantName)}",
                            localizationPayload,
                            Method.POST);
                    }
                    catch (Exception localizationEx)
                    {
                        TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                        {
                            CssClassName = "alert-warning",
                            Title = "Warning.",
                            DisplayMessage = BuildApiErrorMessage(localizationEx, "Store configuration was saved, but tenant localization settings update failed.")
                        });

                        return View(model);
                    }
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(saveMessage)
                        ? (model.IsExistingRecord ? "Store configuration updated successfully" : "Store configuration saved successfully")
                        : saveMessage
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save store configuration")
                });

                return View(model);
            }

            return RedirectToAction(nameof(StoreTimings));
        }

        [HttpGet]
        public async Task<IActionResult> PricingRules()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access pricing rules."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            PricingRulesViewModel model;
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var pricingRulesUrl = $"{(_configuration["Services:Master:BaseUrl"] ?? string.Empty).TrimEnd('/')}/Customer/GetPricingRules";
                var stopwatch = Stopwatch.StartNew();
                var response = await _clientApi.SendRequestAsync<PricingRulesViewModel>("/GetPricingRules", parameters, Method.GET);
                stopwatch.Stop();
                _logger.LogInformation("PricingRules load succeeded in {ElapsedMs} ms. Endpoint: {Endpoint}. Tenant: {TenantName}, Store: {StoreCode}", stopwatch.ElapsedMilliseconds, pricingRulesUrl, tenantName, storeCode);
                model = response ?? BuildDefaultPricingRules(tenantName, storeCode);
            }
            catch (Exception ex) when (IsNotFoundApiResponse(ex))
            {
                model = BuildDefaultPricingRules(tenantName, storeCode);
            }
            catch (ApiUnavailableException ex)
            {
                var pricingRulesUrl = $"{(_configuration["Services:Master:BaseUrl"] ?? string.Empty).TrimEnd('/')}/Customer/GetPricingRules";
                _logger.LogError(ex, "PricingRules load failed due to unavailable API. Endpoint: {Endpoint}. Tenant: {TenantName}, Store: {StoreCode}", pricingRulesUrl, tenantName, storeCode);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Unable to load pricing rules right now. Please try again shortly."
                });

                model = BuildDefaultPricingRules(tenantName, storeCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PricingRules load failed unexpectedly. Tenant: {TenantName}, Store: {StoreCode}", tenantName, storeCode);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Unable to load pricing rules right now. Please try again shortly."
                });

                model = BuildDefaultPricingRules(tenantName, storeCode);
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PricingRules(PricingRulesViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access pricing rules."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!ValidatePricingRules(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SavePricingRules", model, Method.POST);
                var responseStatusCode = response?.StatusCode?.Trim();
                var isSuccess = string.IsNullOrWhiteSpace(responseStatusCode)
                                || string.Equals(responseStatusCode, "200", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(responseStatusCode, "201", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(responseStatusCode, "204", StringComparison.OrdinalIgnoreCase);

                if (!isSuccess)
                {
                    TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                    {
                        CssClassName = "alert alert-danger",
                        Title = "Failed.",
                        DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                            ? "Unable to save pricing rules."
                            : response.Message
                    });

                    return View(model);
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Pricing rules saved successfully."
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save pricing rules.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(PricingRules));
        }

        [HttpGet]
        public IActionResult ServiceItemMaster()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access this page."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            return View(new ServiceItemMasterViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode
            });
        }

        [HttpGet]
        public async Task<IActionResult> TaxInvoiceSettings()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access tax & invoice settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = await LoadTaxConfigurationScreenModelAsync(tenantName, storeCode, autoCreateDefault: true);
            var recommendationContext = await BuildTaxRecommendationContextAsync(tenantName, model.CountryCode, model.TaxMode);
            model.RecommendedTaxMode = recommendationContext.RecommendedTaxMode;
            model.TenantRegisteredCountryCode = recommendationContext.RegisteredCountryCode;
            model.OriginalCountryCode = recommendationContext.OriginalCountryCode;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TaxInvoiceSettings(TaxConfigurationViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access tax & invoice settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);

            var (profileCountryCode, profileCurrencyCode) = await GetTenantCountryCurrencyAsync(tenantName);
            if (!string.IsNullOrWhiteSpace(profileCountryCode))
            {
                model.CountryCode = profileCountryCode;
            }

            if (!string.IsNullOrWhiteSpace(profileCurrencyCode))
            {
                model.CurrencyCode = profileCurrencyCode;
            }

            var recommendationContext = await BuildTaxRecommendationContextAsync(tenantName, model.CountryCode, model.TaxMode);
            model.RecommendedTaxMode = recommendationContext.RecommendedTaxMode;
            model.TenantRegisteredCountryCode = recommendationContext.RegisteredCountryCode;
            model.OriginalCountryCode = string.IsNullOrWhiteSpace(model.OriginalCountryCode)
                ? recommendationContext.OriginalCountryCode
                : model.OriginalCountryCode;

            var configQuery = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            TaxConfigurationDto currentConfig = null;
            try
            {
                currentConfig = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/GetTaxConfiguration", configQuery, Method.GET);

                var existingConfigResponseBody = SafeSerializeForLogging(currentConfig);
                var (existingConfigStatusCode, existingConfigMessage) = TryExtractApiStatusAndMessage(existingConfigResponseBody);
                var existingConfigHttpStatusCode = string.IsNullOrWhiteSpace(existingConfigStatusCode) ? "200" : existingConfigStatusCode;

                _logger?.LogInformation(
                    "GetTaxConfiguration API response. Tenant={TenantName}, Store={StoreCode}, HttpStatusCode={HttpStatusCode}, Message={Message}, ResponseBody={ResponseBody}",
                    tenantName,
                    storeCode,
                    existingConfigHttpStatusCode,
                    existingConfigMessage ?? string.Empty,
                    existingConfigResponseBody);

                if (!string.IsNullOrWhiteSpace(existingConfigStatusCode)
                    && !IsSuccessfulApiStatusCode(existingConfigStatusCode))
                {
                    currentConfig = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "GetTaxConfiguration API call failed during TaxInvoiceSettings POST. Tenant={TenantName}, Store={StoreCode}, HttpStatusCode={HttpStatusCode}, ResponseBody={ResponseBody}",
                    tenantName,
                    storeCode,
                    (ex as ApiUnavailableException)?.StatusCode?.ToString() ?? "NotAvailable",
                    (ex as ApiUnavailableException)?.ResponseContent ?? string.Empty);
            }

            ModelState.Remove(nameof(TaxConfigurationViewModel.ComponentEditor) + "." + nameof(TaxComponentEditorViewModel.Code));
            ModelState.Remove(nameof(TaxConfigurationViewModel.ComponentEditor) + "." + nameof(TaxComponentEditorViewModel.DisplayName));

            if (!ModelState.IsValid)
            {
                var reloaded = await LoadTaxConfigurationScreenModelAsync(tenantName, storeCode, autoCreateDefault: true);
                reloaded.ComponentEditor = model.ComponentEditor ?? new TaxComponentEditorViewModel();
                return View(reloaded);
            }

            if (!ValidateTaxModeSelectionConfirmation(model, out var confirmationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Confirmation Required.",
                    DisplayMessage = confirmationError
                });

                var reloaded = await LoadTaxConfigurationScreenModelAsync(tenantName, storeCode, autoCreateDefault: true);
                reloaded.ComponentEditor = model.ComponentEditor ?? new TaxComponentEditorViewModel();
                reloaded.TaxMode = model.TaxMode;
                reloaded.CountryCode = model.CountryCode;
                reloaded.CurrencyCode = model.CurrencyCode;
                reloaded.IsPricesInclusive = model.IsPricesInclusive;
                reloaded.EnableTax = model.EnableTax;
                reloaded.GstPercent = model.GstPercent;
                reloaded.PricesIncludeTax = model.PricesIncludeTax;
                reloaded.InvoicePrefix = model.InvoicePrefix;
                reloaded.NextInvoiceNumber = model.NextInvoiceNumber;
                reloaded.InvoiceNumberPadding = model.InvoiceNumberPadding;
                reloaded.ResetInvoiceNumberYearly = model.ResetInvoiceNumberYearly;
                reloaded.Notes = model.Notes;
                reloaded.TaxModeOverrideConfirmed = model.TaxModeOverrideConfirmed;
                reloaded.NoTaxConfirmed = model.NoTaxConfirmed;
                reloaded.CountryChangeAcknowledged = model.CountryChangeAcknowledged;
                reloaded.RecommendedTaxMode = model.RecommendedTaxMode;
                reloaded.TenantRegisteredCountryCode = model.TenantRegisteredCountryCode;
                reloaded.OriginalCountryCode = model.OriginalCountryCode;
                reloaded.CgstRatePercent = model.CgstRatePercent;
                reloaded.SgstRatePercent = model.SgstRatePercent;
                return View(reloaded);
            }

            var gstSplitValidationError = ValidateGstSplitAgainstTotal(model, currentConfig);
            if (!string.IsNullOrWhiteSpace(gstSplitValidationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = gstSplitValidationError
                });

                var reloaded = await LoadTaxConfigurationScreenModelAsync(tenantName, storeCode, autoCreateDefault: true);
                reloaded.ComponentEditor = model.ComponentEditor ?? new TaxComponentEditorViewModel();
                reloaded.TaxMode = model.TaxMode;
                reloaded.CountryCode = model.CountryCode;
                reloaded.CurrencyCode = model.CurrencyCode;
                reloaded.IsPricesInclusive = model.IsPricesInclusive;
                reloaded.EnableTax = model.EnableTax;
                reloaded.GstPercent = model.GstPercent;
                reloaded.PricesIncludeTax = model.PricesIncludeTax;
                reloaded.InvoicePrefix = model.InvoicePrefix;
                reloaded.NextInvoiceNumber = model.NextInvoiceNumber;
                reloaded.InvoiceNumberPadding = model.InvoiceNumberPadding;
                reloaded.ResetInvoiceNumberYearly = model.ResetInvoiceNumberYearly;
                reloaded.Notes = model.Notes;
                reloaded.TaxModeOverrideConfirmed = model.TaxModeOverrideConfirmed;
                reloaded.NoTaxConfirmed = model.NoTaxConfirmed;
                reloaded.CountryChangeAcknowledged = model.CountryChangeAcknowledged;
                reloaded.RecommendedTaxMode = model.RecommendedTaxMode;
                reloaded.TenantRegisteredCountryCode = model.TenantRegisteredCountryCode;
                reloaded.OriginalCountryCode = model.OriginalCountryCode;
                reloaded.CgstRatePercent = model.CgstRatePercent;
                reloaded.SgstRatePercent = model.SgstRatePercent;
                return View(reloaded);
            }

            if (!ValidateTaxConfigurationViewModel(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                var reloaded = await LoadTaxConfigurationScreenModelAsync(tenantName, storeCode, autoCreateDefault: true);
                reloaded.ComponentEditor = model.ComponentEditor ?? new TaxComponentEditorViewModel();
                reloaded.CgstRatePercent = model.CgstRatePercent;
                reloaded.SgstRatePercent = model.SgstRatePercent;
                return View(reloaded);
            }

            try
            {
                var request = new TaxConfigurationUpsertRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    TaxMode = model.TaxMode,
                    CountryCode = model.CountryCode,
                    CurrencyCode = model.CurrencyCode,
                    IsPricesInclusive = model.IsPricesInclusive,
                    EnableTax = model.EnableTax,
                    GstPercent = model.GstPercent,
                    PricesIncludeTax = model.PricesIncludeTax,
                    InvoicePrefix = model.InvoicePrefix,
                    NextInvoiceNumber = model.NextInvoiceNumber,
                    InvoiceNumberPadding = model.InvoiceNumberPadding,
                    ResetInvoiceNumberYearly = model.ResetInvoiceNumberYearly,
                    Notes = model.Notes,
                    ConcurrencyToken = model.ConcurrencyToken
                };

                var response = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/UpsertTaxConfiguration", request, Method.POST);
                var upsertResponseBody = SafeSerializeForLogging(response);
                var (upsertStatusCode, upsertMessage) = TryExtractApiStatusAndMessage(upsertResponseBody);
                var upsertHttpStatusCode = string.IsNullOrWhiteSpace(upsertStatusCode) ? "200" : upsertStatusCode;

                _logger?.LogInformation(
                    "UpsertTaxConfiguration API response. Tenant={TenantName}, Store={StoreCode}, HttpStatusCode={HttpStatusCode}, Message={Message}, ResponseBody={ResponseBody}",
                    tenantName,
                    storeCode,
                    upsertHttpStatusCode,
                    upsertMessage ?? string.Empty,
                    upsertResponseBody);

                var hasValidUpsertPayload = response != null
                    && !string.IsNullOrWhiteSpace(response.TenantName)
                    && !string.IsNullOrWhiteSpace(response.StoreCode);
                if (!IsApiCallSuccessful(upsertStatusCode, hasValidUpsertPayload))
                {
                    throw new InvalidOperationException(
                        $"UpsertTaxConfiguration failed. HttpStatusCode={upsertHttpStatusCode}. Message={upsertMessage ?? string.Empty}. ResponseBody={upsertResponseBody}");
                }

                await UpsertIndiaGstComponentsAsync(tenantName, storeCode, model, currentConfig ?? response);

                var auditMessage = BuildTaxModeAuditMessage(model);
                if (!string.IsNullOrWhiteSpace(auditMessage))
                {
                    await LogOrderActionAuditAsync(tenantName, storeCode, "TAX-CONFIG", "TAX-CONFIG", "TaxModeSelectionReview", auditMessage);
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = "Tax and invoice settings saved successfully."
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "TaxInvoiceSettings POST failed. Tenant={TenantName}, Store={StoreCode}, ModelTaxMode={TaxMode}, ModelCountryCode={CountryCode}",
                    tenantName,
                    storeCode,
                    model?.TaxMode ?? string.Empty,
                    model?.CountryCode ?? string.Empty);

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save tax and invoice settings.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(TaxInvoiceSettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTaxComponent(TaxConfigurationViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can manage tax components."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            if (!ValidateTaxComponentEditor(model?.ComponentEditor, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return RedirectToAction(nameof(TaxInvoiceSettings));
            }

            try
            {
                var editor = model.ComponentEditor;
                var request = new TaxInvoiceComponentUpsertRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ComponentId = editor.ComponentId,
                    Code = editor.Code,
                    DisplayName = editor.DisplayName,
                    RatePercent = editor.RatePercent,
                    Jurisdiction = editor.Jurisdiction,
                    Priority = editor.Priority,
                    IsCompound = editor.IsCompound,
                    IsActive = editor.IsActive,
                    EffectiveFromUtc = ParseUtcDateTimeOrNull(editor.EffectiveFromUtcText),
                    EffectiveToUtc = ParseUtcDateTimeOrNull(editor.EffectiveToUtcText),
                    ConcurrencyToken = editor.ConcurrencyToken
                };

                if (editor.IsEdit && editor.ComponentId.HasValue)
                {
                    await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/UpdateTaxInvoiceComponent", request, Method.PUT);
                }
                else
                {
                    await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/AddTaxInvoiceComponent", request, Method.POST);
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = "Tax component saved successfully."
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save tax component.")
                });
            }

            return RedirectToAction(nameof(TaxInvoiceSettings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateTaxComponent(int componentId, string concurrencyToken)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can manage tax components."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            try
            {
                var request = new TaxInvoiceComponentDeactivateRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ComponentId = componentId,
                    ConcurrencyToken = concurrencyToken
                };

                await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/DeactivateTaxInvoiceComponent", request, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = "Tax component deactivated successfully."
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to deactivate tax component.")
                });
            }

            return RedirectToAction(nameof(TaxInvoiceSettings));
        }

        private async Task<TaxConfigurationViewModel> LoadTaxConfigurationScreenModelAsync(string tenantName, string storeCode, bool autoCreateDefault)
        {
            var companyName = await GetCompanyNameAsync(tenantName, storeCode);
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            TaxConfigurationDto config = null;
            try
            {
                config = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/GetTaxConfiguration", parameters, Method.GET);
            }
            catch
            {
            }

            var isAutoCreated = false;
            if (config == null && autoCreateDefault)
            {
                var defaultRequest = await BuildDefaultTaxConfigurationRequestAsync(tenantName, storeCode, companyName);
                try
                {
                    await _clientApi.SendRequestAsync<TaxConfigurationDto>("/UpsertTaxConfiguration", defaultRequest, Method.POST);
                    config = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/GetTaxConfiguration", parameters, Method.GET);
                    isAutoCreated = true;
                }
                catch
                {
                }
            }

            var resolvedMode = config?.TaxMode;
            try
            {
                var modeResolution = await _clientApi.SendRequestAsync<TaxModeResolutionDto>("/ResolveTaxMode", parameters, Method.GET);
                if (!string.IsNullOrWhiteSpace(modeResolution?.TaxMode))
                {
                    resolvedMode = modeResolution.TaxMode;
                }
            }
            catch
            {
            }

            if (config == null)
            {
                var defaults = await BuildDefaultTaxConfigurationRequestAsync(tenantName, storeCode, companyName);
                return new TaxConfigurationViewModel
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    CompanyName = companyName,
                    TaxMode = defaults.TaxMode,
                    CountryCode = defaults.CountryCode,
                    CurrencyCode = defaults.CurrencyCode,
                    IsPricesInclusive = defaults.IsPricesInclusive,
                    EnableTax = defaults.EnableTax ?? false,
                    GstPercent = defaults.GstPercent ?? 0,
                    PricesIncludeTax = defaults.PricesIncludeTax ?? false,
                    InvoicePrefix = defaults.InvoicePrefix,
                    NextInvoiceNumber = defaults.NextInvoiceNumber ?? 1,
                    InvoiceNumberPadding = defaults.InvoiceNumberPadding ?? 4,
                    ResetInvoiceNumberYearly = defaults.ResetInvoiceNumberYearly ?? false,
                    Notes = defaults.Notes,
                    ResolvedTaxMode = defaults.TaxMode,
                    IsAutoCreatedDefault = isAutoCreated,
                    ComponentEditor = new TaxComponentEditorViewModel()
                };
            }

            var model = new TaxConfigurationViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                CompanyName = companyName,
                TaxMode = string.IsNullOrWhiteSpace(config.TaxMode) ? "GST" : config.TaxMode,
                CountryCode = string.IsNullOrWhiteSpace(config.CountryCode) ? "IN" : config.CountryCode,
                CurrencyCode = string.IsNullOrWhiteSpace(config.CurrencyCode) ? "INR" : config.CurrencyCode,
                IsPricesInclusive = config.IsPricesInclusive,
                EnableTax = config.EnableTax,
                GstPercent = ResolveTotalTaxPercent(config),
                PricesIncludeTax = config.PricesIncludeTax,
                InvoicePrefix = string.IsNullOrWhiteSpace(config.InvoicePrefix) ? BuildStoreInvoicePrefix(companyName, storeCode) : config.InvoicePrefix,
                NextInvoiceNumber = config.NextInvoiceNumber < 1 ? 1 : config.NextInvoiceNumber,
                InvoiceNumberPadding = config.InvoiceNumberPadding < 1 || config.InvoiceNumberPadding > 12 ? 4 : config.InvoiceNumberPadding,
                ResetInvoiceNumberYearly = config.ResetInvoiceNumberYearly,
                Notes = config.Notes,
                ConcurrencyToken = config.ConcurrencyToken,
                LegacyFallbackSource = config.LegacyFallbackSource,
                ResolvedTaxMode = string.IsNullOrWhiteSpace(resolvedMode) ? config.TaxMode : resolvedMode,
                IsAutoCreatedDefault = isAutoCreated
            };

            model.Components = (config.Components ?? new List<TaxInvoiceComponentDto>())
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Select(x => new TaxConfigurationComponentViewModel
                {
                    Id = x.Id,
                    Code = x.Code,
                    DisplayName = x.DisplayName,
                    RatePercent = x.RatePercent,
                    Jurisdiction = x.Jurisdiction,
                    Priority = x.Priority,
                    IsCompound = x.IsCompound,
                    IsActive = x.IsActive,
                    EffectiveFromUtcText = x.EffectiveFromUtc?.ToString("yyyy-MM-ddTHH:mm"),
                    EffectiveToUtcText = x.EffectiveToUtc?.ToString("yyyy-MM-ddTHH:mm"),
                    ConcurrencyToken = x.ConcurrencyToken
                })
                .ToList();

            var activeGstComponents = model.Components
                .Where(x => x.IsActive)
                .ToList();

            model.CgstRatePercent = activeGstComponents
                .Where(x => string.Equals(x.Code, "CGST", StringComparison.OrdinalIgnoreCase))
                .Select(x => (decimal?)x.RatePercent)
                .FirstOrDefault();

            model.SgstRatePercent = activeGstComponents
                .Where(x => string.Equals(x.Code, "SGST", StringComparison.OrdinalIgnoreCase))
                .Select(x => (decimal?)x.RatePercent)
                .FirstOrDefault();

            model.ComponentEditor = new TaxComponentEditorViewModel
            {
                Priority = model.Components.Count == 0 ? 1 : model.Components.Max(x => x.Priority) + 1,
                IsActive = true
            };

            return model;
        }

        private async Task<(string RecommendedTaxMode, string RegisteredCountryCode, string OriginalCountryCode)> BuildTaxRecommendationContextAsync(string tenantName, string modelCountryCode, string currentTaxMode)
        {
            var (registeredCountryCode, _) = await GetTenantCountryCurrencyAsync(tenantName);
            var normalizedRegisteredCountry = string.IsNullOrWhiteSpace(registeredCountryCode)
                ? "ZZ"
                : registeredCountryCode.Trim().ToUpperInvariant();

            var recommendedMode = ResolveTaxModeByCountry(normalizedRegisteredCountry);

            var normalizedModelCountry = string.IsNullOrWhiteSpace(modelCountryCode)
                ? normalizedRegisteredCountry
                : modelCountryCode.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(currentTaxMode))
            {
                currentTaxMode = recommendedMode;
            }

            return (recommendedMode, normalizedRegisteredCountry, normalizedModelCountry);
        }

        private static string ResolveTaxModeByCountry(string countryCode)
        {
            switch ((countryCode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "US":
                    return "SalesTax";
                case "GB":
                case "AE":
                case "DE":
                case "FR":
                    return "VAT";
                case "IN":
                case "IND":
                    return "GST";
                default:
                    return "NoTax";
            }
        }

        private static bool ValidateTaxModeSelectionConfirmation(TaxConfigurationViewModel model, out string message)
        {
            message = null;
            if (model == null)
            {
                message = "Invalid tax selection.";
                return false;
            }

            var selectedMode = (model.TaxMode ?? string.Empty).Trim();
            var recommendedMode = (model.RecommendedTaxMode ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(recommendedMode)
                && !string.Equals(selectedMode, recommendedMode, StringComparison.OrdinalIgnoreCase)
                && !model.TaxModeOverrideConfirmed)
            {
                message = "The selected tax system differs from the country recommendation. Please confirm this override before saving.";
                return false;
            }

            if (string.Equals(selectedMode, "NoTax", StringComparison.OrdinalIgnoreCase)
                && !model.NoTaxConfirmed)
            {
                message = "Please confirm No Tax selection before saving.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.OriginalCountryCode)
                && !string.IsNullOrWhiteSpace(model.CountryCode)
                && !string.Equals(model.OriginalCountryCode, model.CountryCode, StringComparison.OrdinalIgnoreCase)
                && !model.CountryChangeAcknowledged)
            {
                message = "Country has changed. Please acknowledge that tax settings were reviewed before saving.";
                return false;
            }

            return true;
        }

        private static string ValidateGstSplitAgainstTotal(TaxConfigurationViewModel model, TaxConfigurationDto currentConfig)
        {
            if (model == null
                || !model.EnableTax
                || !string.Equals(model.TaxMode, "GST", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(model.CountryCode, "IN", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var cgstRate = model.CgstRatePercent;
            var sgstRate = model.SgstRatePercent;

            if (!cgstRate.HasValue || !sgstRate.HasValue)
            {
                var activeComponents = (currentConfig?.Components ?? new List<TaxInvoiceComponentDto>())
                    .Where(c => c.IsActive)
                    .ToList();

                cgstRate ??= activeComponents
                    .Where(c => string.Equals(c.Code, "CGST", StringComparison.OrdinalIgnoreCase))
                    .Select(c => (decimal?)c.RatePercent)
                    .FirstOrDefault();

                sgstRate ??= activeComponents
                    .Where(c => string.Equals(c.Code, "SGST", StringComparison.OrdinalIgnoreCase))
                    .Select(c => (decimal?)c.RatePercent)
                    .FirstOrDefault();
            }

            if (!cgstRate.HasValue && !sgstRate.HasValue)
            {
                return null;
            }

            if (!cgstRate.HasValue || !sgstRate.HasValue)
            {
                return "Enter both CGST and SGST rates for India GST configuration.";
            }

            if (cgstRate.Value < 0m)
            {
                return "CGST rate cannot be negative.";
            }

            if (sgstRate.Value < 0m)
            {
                return "SGST rate cannot be negative.";
            }

            if (model.GstPercent < 0m)
            {
                return "Total GST rate cannot be negative.";
            }

            var combinedSplitPercent = Math.Round(cgstRate.Value + sgstRate.Value, 2, MidpointRounding.AwayFromZero);
            var totalTaxPercent = Math.Round(model.GstPercent, 2, MidpointRounding.AwayFromZero);

            if (combinedSplitPercent <= 0)
            {
                return null;
            }

            if (combinedSplitPercent > totalTaxPercent)
            {
                return $"CGST + SGST total ({combinedSplitPercent:0.##}%) cannot exceed Total Tax Rate ({totalTaxPercent:0.##}%). Update Total Tax Rate or component rates before saving.";
            }

            if (combinedSplitPercent < totalTaxPercent)
            {
                return $"CGST + SGST total ({combinedSplitPercent:0.##}%) must equal Total Tax Rate ({totalTaxPercent:0.##}%) before saving.";
            }

            return null;
        }

        private async Task UpsertIndiaGstComponentsAsync(string tenantName, string storeCode, TaxConfigurationViewModel model, TaxConfigurationDto currentConfig)
        {
            if (model == null
                || !string.Equals(model.TaxMode, "GST", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(model.CountryCode, "IN", StringComparison.OrdinalIgnoreCase)
                || !model.CgstRatePercent.HasValue
                || !model.SgstRatePercent.HasValue)
            {
                return;
            }

            var componentIndex = (currentConfig?.Components ?? new List<TaxInvoiceComponentDto>())
                .Where(c => c.IsActive
                    && (string.Equals(c.Code, "CGST", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Code, "SGST", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Priority)
                .ThenBy(c => c.Id)
                .ToList();

            var highestPriority = (currentConfig?.Components ?? new List<TaxInvoiceComponentDto>())
                .DefaultIfEmpty()
                .Max(c => c == null ? 0 : c.Priority);

            async Task SaveComponentAsync(string code, string displayName, decimal ratePercent, int defaultPriority)
            {
                var existing = componentIndex.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

                var request = new TaxInvoiceComponentUpsertRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ComponentId = existing?.Id,
                    Code = code,
                    DisplayName = string.IsNullOrWhiteSpace(existing?.DisplayName) ? displayName : existing.DisplayName,
                    RatePercent = ratePercent,
                    Jurisdiction = string.IsNullOrWhiteSpace(existing?.Jurisdiction) ? "IN" : existing.Jurisdiction,
                    Priority = existing?.Priority > 0 ? existing.Priority : Math.Max(defaultPriority, highestPriority + 1),
                    IsCompound = existing?.IsCompound ?? false,
                    IsActive = true,
                    EffectiveFromUtc = existing?.EffectiveFromUtc,
                    EffectiveToUtc = existing?.EffectiveToUtc,
                    ConcurrencyToken = existing?.ConcurrencyToken
                };

                if (existing != null)
                {
                    var updateResponse = await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/UpdateTaxInvoiceComponent", request, Method.PUT);
                    var updateResponseBody = SafeSerializeForLogging(updateResponse);
                    var (updateStatusCode, updateMessage) = TryExtractApiStatusAndMessage(updateResponseBody);
                    var updateHttpStatusCode = string.IsNullOrWhiteSpace(updateStatusCode) ? "200" : updateStatusCode;

                    _logger?.LogInformation(
                        "Tax component save API response. Tenant={TenantName}, Store={StoreCode}, ComponentCode={ComponentCode}, Operation=UpdateTaxInvoiceComponent, HttpStatusCode={HttpStatusCode}, Message={Message}, ResponseBody={ResponseBody}",
                        tenantName,
                        storeCode,
                        code,
                        updateHttpStatusCode,
                        updateMessage ?? string.Empty,
                        updateResponseBody);

                    var hasValidUpdatePayload = updateResponse != null && !string.IsNullOrWhiteSpace(updateResponse.Code);
                    if (!IsApiCallSuccessful(updateStatusCode, hasValidUpdatePayload))
                    {
                        throw new InvalidOperationException(
                            $"UpdateTaxInvoiceComponent failed for {code}. HttpStatusCode={updateHttpStatusCode}. Message={updateMessage ?? string.Empty}. ResponseBody={updateResponseBody}");
                    }
                }
                else
                {
                    var addResponse = await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/AddTaxInvoiceComponent", request, Method.POST);
                    var addResponseBody = SafeSerializeForLogging(addResponse);
                    var (addStatusCode, addMessage) = TryExtractApiStatusAndMessage(addResponseBody);
                    var addHttpStatusCode = string.IsNullOrWhiteSpace(addStatusCode) ? "200" : addStatusCode;

                    _logger?.LogInformation(
                        "Tax component save API response. Tenant={TenantName}, Store={StoreCode}, ComponentCode={ComponentCode}, Operation=AddTaxInvoiceComponent, HttpStatusCode={HttpStatusCode}, Message={Message}, ResponseBody={ResponseBody}",
                        tenantName,
                        storeCode,
                        code,
                        addHttpStatusCode,
                        addMessage ?? string.Empty,
                        addResponseBody);

                    var hasValidAddPayload = addResponse != null && !string.IsNullOrWhiteSpace(addResponse.Code);
                    if (!IsApiCallSuccessful(addStatusCode, hasValidAddPayload))
                    {
                        throw new InvalidOperationException(
                            $"AddTaxInvoiceComponent failed for {code}. HttpStatusCode={addHttpStatusCode}. Message={addMessage ?? string.Empty}. ResponseBody={addResponseBody}");
                    }
                }
            }

            await SaveComponentAsync("CGST", "Central GST", model.CgstRatePercent.Value, 1);
            await SaveComponentAsync("SGST", "State GST", model.SgstRatePercent.Value, 2);
        }

        private static string BuildTaxModeAuditMessage(TaxConfigurationViewModel model)
        {
            if (model == null)
            {
                return null;
            }

            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(model.RecommendedTaxMode)
                && !string.Equals(model.TaxMode, model.RecommendedTaxMode, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add($"Override confirmed. Selected: {model.TaxMode}, Recommended: {model.RecommendedTaxMode}, Country: {model.CountryCode}");
            }

            if (string.Equals(model.TaxMode, "NoTax", StringComparison.OrdinalIgnoreCase) && model.NoTaxConfirmed)
            {
                tokens.Add("No Tax selection explicitly confirmed by administrator.");
            }

            if (!string.IsNullOrWhiteSpace(model.OriginalCountryCode)
                && !string.Equals(model.OriginalCountryCode, model.CountryCode, StringComparison.OrdinalIgnoreCase)
                && model.CountryChangeAcknowledged)
            {
                tokens.Add($"Country changed from {model.OriginalCountryCode} to {model.CountryCode}; review acknowledged.");
            }

            return tokens.Count == 0 ? null : string.Join(" ", tokens);
        }

        private async Task<bool> EnsureIndiaGstDefaultComponentsAsync(TaxConfigurationDto config, string tenantName, string storeCode)
        {
            if (config == null)
            {
                return false;
            }

            if (!string.Equals(config.TaxMode, "GST", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(config.CountryCode, "IN", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((config.Components?.Count ?? 0) > 0)
            {
                return false;
            }

            if (config.GstPercent <= 0)
            {
                return false;
            }

            var cgstRate = Math.Round(config.GstPercent / 2m, 2, MidpointRounding.AwayFromZero);
            var sgstRate = Math.Round(config.GstPercent - cgstRate, 2, MidpointRounding.AwayFromZero);

            var defaults = new[]
            {
                new TaxInvoiceComponentUpsertRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    Code = "CGST",
                    DisplayName = "Central GST",
                    RatePercent = cgstRate,
                    Jurisdiction = "IN",
                    Priority = 1,
                    IsCompound = false,
                    IsActive = true
                },
                new TaxInvoiceComponentUpsertRequestDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    Code = "SGST",
                    DisplayName = "State GST",
                    RatePercent = sgstRate,
                    Jurisdiction = "IN",
                    Priority = 2,
                    IsCompound = false,
                    IsActive = true
                }
            };

            var createdCount = 0;
            foreach (var component in defaults)
            {
                try
                {
                    await _clientApi.SendRequestAsync<TaxInvoiceComponentDto>("/AddTaxInvoiceComponent", component, Method.POST);
                    createdCount++;
                }
                catch
                {
                }
            }

            return createdCount > 0;
        }

        private static decimal ResolveTotalTaxPercent(TaxConfigurationDto config)
        {
            if (config == null)
            {
                return 0;
            }

            var activeComponents = (config.Components ?? new List<TaxInvoiceComponentDto>())
                .Where(x => x.IsActive)
                .ToList();

            return activeComponents.Count > 0
                ? activeComponents.Sum(x => x.RatePercent)
                : config.GstPercent;
        }

        private sealed class CreateOrderTaxComponentContext
        {
            public string Code { get; set; }
            public string DisplayName { get; set; }
            public decimal RatePercent { get; set; }
            public string Jurisdiction { get; set; }
            public int Priority { get; set; }
            public bool IsCompound { get; set; }
        }

        private sealed class CreateOrderTaxContext
        {
            public string TaxMode { get; set; } = "NoTax";
            public bool EnableTax { get; set; }
            public bool PricesIncludeTax { get; set; }
            public decimal TotalRatePercent { get; set; }
            public List<CreateOrderTaxComponentContext> Components { get; set; } = new List<CreateOrderTaxComponentContext>();
            public bool IsConfigurationMissing { get; set; }
        }

        private sealed class CreateOrderTaxComputation
        {
            public decimal SubTotal { get; set; }
            public decimal TaxAmount { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TaxPercent { get; set; }
            public decimal CgstAmount { get; set; }
            public decimal SgstAmount { get; set; }
        }

        private async Task<CreateOrderTaxContext> LoadCreateOrderTaxContextAsync(string tenantName, string storeCode)
        {
            var context = new CreateOrderTaxContext();
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return context;
            }

            TaxConfigurationDto config = null;
            try
            {
                config = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/GetTaxConfiguration", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode }
                }, Method.GET);
            }
            catch
            {
            }

            if (config == null)
            {
                try
                {
                    var companyName = await GetCompanyNameAsync(tenantName, storeCode);
                    var defaultRequest = await BuildDefaultTaxConfigurationRequestAsync(tenantName, storeCode, companyName);
                    await _clientApi.SendRequestAsync<TaxConfigurationDto>("/UpsertTaxConfiguration", defaultRequest, Method.POST);

                    config = await _clientApi.SendRequestAsync<TaxConfigurationDto>("/GetTaxConfiguration", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode }
                    }, Method.GET);
                }
                catch
                {
                }

                if (config == null)
                {
                    context.IsConfigurationMissing = true;
                    return context;
                }
            }

            var activeComponents = (config.Components ?? new List<TaxInvoiceComponentDto>())
                .Where(x => x != null && x.IsActive && x.RatePercent > 0m)
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .Select(x => new CreateOrderTaxComponentContext
                {
                    Code = x.Code?.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? (x.Code ?? "Tax") : x.DisplayName.Trim(),
                    RatePercent = Math.Round(Math.Max(0m, x.RatePercent), 4, MidpointRounding.AwayFromZero),
                    Jurisdiction = x.Jurisdiction?.Trim(),
                    Priority = x.Priority,
                    IsCompound = x.IsCompound
                })
                .ToList();

            var totalRate = activeComponents.Count > 0
                ? activeComponents.Sum(x => x.RatePercent)
                : Math.Round(Math.Max(0m, config.GstPercent), 4, MidpointRounding.AwayFromZero);

            var rawMode = string.IsNullOrWhiteSpace(config.TaxMode) ? string.Empty : config.TaxMode.Trim();
            var normalizedMode = string.Equals(rawMode, "VAT", StringComparison.OrdinalIgnoreCase)
                ? "VAT"
                : string.Equals(rawMode, "GST", StringComparison.OrdinalIgnoreCase)
                    ? "GST"
                    : string.Equals(rawMode, "SalesTax", StringComparison.OrdinalIgnoreCase)
                        ? "SalesTax"
                        : "NoTax";

            var enableTax = config.EnableTax && totalRate > 0m && !string.Equals(normalizedMode, "NoTax", StringComparison.OrdinalIgnoreCase);
            context.TaxMode = normalizedMode;
            context.EnableTax = enableTax;
            context.PricesIncludeTax = config.PricesIncludeTax;
            context.TotalRatePercent = enableTax ? Math.Round(Math.Max(0m, totalRate), 4, MidpointRounding.AwayFromZero) : 0m;

            if (enableTax)
            {
                if (activeComponents.Count == 0 && context.TotalRatePercent > 0m)
                {
                    activeComponents.Add(new CreateOrderTaxComponentContext
                    {
                        Code = normalizedMode,
                        DisplayName = string.Equals(normalizedMode, "VAT", StringComparison.OrdinalIgnoreCase)
                            ? "VAT"
                            : string.Equals(normalizedMode, "SalesTax", StringComparison.OrdinalIgnoreCase)
                                ? "Sales Tax"
                                : "Tax",
                        RatePercent = context.TotalRatePercent,
                        Jurisdiction = string.Empty,
                        Priority = 1,
                        IsCompound = false
                    });
                }

                context.Components = activeComponents;
            }

            return context;
        }

        private static CreateOrderTaxComputation ComputeCreateOrderTaxSummary(decimal orderAmount, CreateOrderTaxContext context)
        {
            var gross = Math.Round(Math.Max(0m, orderAmount), 2, MidpointRounding.AwayFromZero);
            var safeContext = context ?? new CreateOrderTaxContext();
            if (!safeContext.EnableTax || safeContext.TotalRatePercent <= 0m)
            {
                return new CreateOrderTaxComputation
                {
                    SubTotal = gross,
                    TaxAmount = 0m,
                    TotalAmount = gross,
                    TaxPercent = 0m,
                    CgstAmount = 0m,
                    SgstAmount = 0m
                };
            }

            var rate = safeContext.TotalRatePercent / 100m;
            var subTotal = safeContext.PricesIncludeTax
                ? Math.Round(gross / (1m + rate), 2, MidpointRounding.AwayFromZero)
                : gross;
            var totalAmount = safeContext.PricesIncludeTax
                ? gross
                : Math.Round(gross * (1m + rate), 2, MidpointRounding.AwayFromZero);
            var taxAmount = Math.Round(Math.Max(0m, totalAmount - subTotal), 2, MidpointRounding.AwayFromZero);

            decimal cgstAmount = 0m;
            decimal sgstAmount = 0m;
            if ((safeContext.Components?.Count ?? 0) > 0 && taxAmount > 0m)
            {
                var normalizedComponents = safeContext.Components
                    .Where(x => x != null && x.RatePercent > 0m)
                    .OrderBy(x => x.Priority)
                    .ThenBy(x => x.Code)
                    .ToList();

                var totalComponentRate = normalizedComponents.Sum(x => x.RatePercent);
                var allocatedTax = 0m;
                for (var i = 0; i < normalizedComponents.Count; i++)
                {
                    var component = normalizedComponents[i];
                    var componentTax = i == normalizedComponents.Count - 1
                        ? Math.Round(Math.Max(0m, taxAmount - allocatedTax), 2, MidpointRounding.AwayFromZero)
                        : Math.Round(taxAmount * component.RatePercent / totalComponentRate, 2, MidpointRounding.AwayFromZero);

                    allocatedTax = Math.Round(allocatedTax + componentTax, 2, MidpointRounding.AwayFromZero);

                    if (string.Equals(component.Code, "CGST", StringComparison.OrdinalIgnoreCase))
                    {
                        cgstAmount = componentTax;
                    }
                    else if (string.Equals(component.Code, "SGST", StringComparison.OrdinalIgnoreCase))
                    {
                        sgstAmount = componentTax;
                    }
                }
            }

            return new CreateOrderTaxComputation
            {
                SubTotal = subTotal,
                TaxAmount = taxAmount,
                TotalAmount = totalAmount,
                TaxPercent = Math.Round(Math.Max(0m, safeContext.TotalRatePercent), 2, MidpointRounding.AwayFromZero),
                CgstAmount = Math.Round(Math.Max(0m, cgstAmount), 2, MidpointRounding.AwayFromZero),
                SgstAmount = Math.Round(Math.Max(0m, sgstAmount), 2, MidpointRounding.AwayFromZero)
            };
        }

        private async Task<TaxConfigurationUpsertRequestDto> BuildDefaultTaxConfigurationRequestAsync(string tenantName, string storeCode, string companyName)
        {
            var (countryCode, currencyCode) = await GetTenantCountryCurrencyAsync(tenantName);
            var normalizedCountry = string.IsNullOrWhiteSpace(countryCode) ? string.Empty : countryCode.Trim().ToUpperInvariant();
            var normalizedCurrency = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();

            var mode = ResolveTaxModeByCountry(normalizedCountry);
            switch ((normalizedCountry ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "US":
                    normalizedCurrency ??= "USD";
                    break;
                case "GB":
                    normalizedCurrency ??= "GBP";
                    break;
                case "AE":
                    normalizedCurrency ??= "AED";
                    break;
                case "DE":
                case "FR":
                    normalizedCurrency ??= "EUR";
                    break;
                case "IN":
                    normalizedCurrency ??= "INR";
                    break;
                default:
                    normalizedCountry = string.IsNullOrWhiteSpace(normalizedCountry) ? "ZZ" : normalizedCountry;
                    normalizedCurrency ??= "USD";
                    break;
            }

            return new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                TaxMode = mode,
                CountryCode = normalizedCountry,
                CurrencyCode = normalizedCurrency,
                IsPricesInclusive = false,
                EnableTax = false,
                GstPercent = 0m,
                PricesIncludeTax = false,
                InvoicePrefix = BuildStoreInvoicePrefix(companyName, storeCode),
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                Notes = null
            };
        }

        private async Task<(string CountryCode, string CurrencyCode)> GetTenantCountryCurrencyAsync(string tenantName)
        {
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                return (null, null);
            }

            try
            {
                var parms = new Dictionary<string, string>
                {
                    { "eMail", tenantName }
                };

                var profile = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.GET);
                var countryCode = NormalizeTenantCountryCode(profile?.CountryCode, profile?.Country);
                var currencyCode = profile?.CurrencyCode;
                return (countryCode, currencyCode);
            }
            catch
            {
                return (null, null);
            }
        }

        private static string NormalizeTenantCountryCode(string countryCode, string countryName)
        {
            if (!string.IsNullOrWhiteSpace(countryCode))
            {
                var normalizedCode = countryCode.Trim().ToUpperInvariant();

                switch (normalizedCode)
                {
                    case "IN":
                    case "IND":
                        return "IN";
                    case "US":
                    case "USA":
                        return "US";
                    case "GB":
                    case "GBR":
                    case "UK":
                        return "GB";
                    case "AE":
                    case "ARE":
                    case "UAE":
                        return "AE";
                    case "DE":
                    case "DEU":
                        return "DE";
                    case "FR":
                    case "FRA":
                        return "FR";
                }

                if (normalizedCode.Length == 2)
                {
                    return normalizedCode;
                }
            }

            if (string.IsNullOrWhiteSpace(countryName))
            {
                return null;
            }

            switch (countryName.Trim().ToUpperInvariant())
            {
                case "INDIA":
                    return "IN";
                case "UNITED STATES":
                case "UNITED STATES OF AMERICA":
                case "USA":
                    return "US";
                case "UNITED KINGDOM":
                case "GREAT BRITAIN":
                case "UK":
                    return "GB";
                case "UNITED ARAB EMIRATES":
                case "UAE":
                    return "AE";
                case "GERMANY":
                    return "DE";
                case "FRANCE":
                    return "FR";
                default:
                    return countryCode;
            }
        }

        private static bool ValidateTaxConfigurationViewModel(TaxConfigurationViewModel model, out string validationError)
        {
            validationError = null;

            if (model == null)
            {
                validationError = "Invalid tax configuration request.";
                return false;
            }

            var taxMode = model.TaxMode?.Trim();
            if (string.IsNullOrWhiteSpace(taxMode)
                || !(string.Equals(taxMode, "NoTax", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(taxMode, "GST", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(taxMode, "VAT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(taxMode, "SalesTax", StringComparison.OrdinalIgnoreCase)))
            {
                validationError = "Select a valid tax mode.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.CountryCode) || model.CountryCode.Trim().Length != 2)
            {
                validationError = "Country code must be a valid 2-letter ISO code.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.CurrencyCode) || model.CurrencyCode.Trim().Length < 3)
            {
                validationError = "Currency code is required.";
                return false;
            }

            if (model.EnableTax && model.GstPercent <= 0)
            {
                validationError = "Tax percentage must be greater than 0 when tax is enabled.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.InvoicePrefix)
                && !model.InvoicePrefix.Trim().All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '/'))
            {
                validationError = "Invoice prefix can contain only letters, numbers, hyphen and slash.";
                return false;
            }

            return true;
        }

        private static bool ValidateTaxComponentEditor(TaxComponentEditorViewModel model, out string validationError)
        {
            validationError = null;
            if (model == null)
            {
                validationError = "Invalid tax component request.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.Code))
            {
                validationError = "Component code is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                validationError = "Component display name is required.";
                return false;
            }

            if (model.RatePercent < 0 || model.RatePercent > 100)
            {
                validationError = "Component rate must be between 0 and 100.";
                return false;
            }

            if (model.Priority < 1)
            {
                validationError = "Priority must be 1 or greater.";
                return false;
            }

            return true;
        }

        private static DateTime? ParseUtcDateTimeOrNull(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return null;
            }

            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        private async Task<TaxInvoiceSettingsViewModel> LoadTaxInvoiceSettingsAsync(string tenantName, string storeCode)
        {
            TaxInvoiceSettingsViewModel model;
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<TaxInvoiceSettingsViewModel>("/GetTaxInvoiceSettings", parameters, Method.GET);
                model = response ?? BuildDefaultTaxInvoiceSettings(tenantName, storeCode);
            }
            catch
            {
                model = BuildDefaultTaxInvoiceSettings(tenantName, storeCode);
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);

            if (model.NextInvoiceNumber < 1)
            {
                model.NextInvoiceNumber = 1;
            }

            if (model.InvoiceNumberPadding < 1 || model.InvoiceNumberPadding > 12)
            {
                model.InvoiceNumberPadding = 4;
            }

            if (string.IsNullOrWhiteSpace(model.InvoicePrefix))
            {
                model.InvoicePrefix = BuildStoreInvoicePrefix(model.CompanyName, storeCode);
            }

            return model;
        }

        /// <summary>
        /// Resolves the registered company name (Tenants.TenantName) for the logged in tenant.
        /// Falls back to the configured store name and finally to the store code.
        /// </summary>
        private async Task<string> GetCompanyNameAsync(string tenantEmail, string storeCode)
        {
            if (!string.IsNullOrWhiteSpace(tenantEmail))
            {
                try
                {
                    var parms = new Dictionary<string, string>
                    {
                        { "eMail", tenantEmail }
                    };

                    var profile = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.GET);
                    if (!string.IsNullOrWhiteSpace(profile?.TenantName))
                    {
                        return profile.TenantName;
                    }
                }
                catch
                {
                    // Fall through to the store configuration when the identity API is unavailable.
                }
            }

            try
            {
                var storeConfig = await _storeConfigurationService.LoadAsync(tenantEmail, storeCode);
                if (!string.IsNullOrWhiteSpace(storeConfig?.StoreName))
                {
                    return storeConfig.StoreName;
                }
            }
            catch
            {
                // Fall through to the store code when the configuration cannot be loaded.
            }

            return storeCode;
        }

        /// <summary>
        /// Builds a readable invoice prefix from the company name and the store, for example "RAMAK-YLJ-".
        /// </summary>
        private static string BuildStoreInvoicePrefix(string companyName, string storeCode)
        {
            static string Shorten(string value, int maxLength)
            {
                var cleaned = new string((value ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                    .ToUpperInvariant();

                return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
            }

            var companyPart = Shorten(companyName, 5);
            var storePart = Shorten(storeCode, 6);

            if (companyPart.Length == 0 && storePart.Length == 0)
            {
                return "INV-";
            }

            if (companyPart.Length == 0)
            {
                return storePart + "-";
            }

            if (storePart.Length == 0)
            {
                return companyPart + "-";
            }

            return companyPart + "-" + storePart + "-";
        }

        private static TaxInvoiceSettingsViewModel BuildDefaultTaxInvoiceSettings(string tenantName, string storeCode)
            => new TaxInvoiceSettingsViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableTax = false,
                GstPercent = 0,
                PricesIncludeTax = false,
                InvoicePrefix = null,
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false
            };

        private static bool ValidateTaxInvoiceSettings(TaxInvoiceSettingsViewModel model, out string validationError)
        {
            validationError = null;

            if (model.EnableTax && model.GstPercent <= 0)
            {
                validationError = "Please enter a GST percentage greater than 0 when tax is applied.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.InvoicePrefix)
                && !model.InvoicePrefix.Trim().All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '/'))
            {
                validationError = "Invoice prefix can contain only letters, numbers, hyphen and slash.";
                return false;
            }

            return true;
        }

        [HttpGet]
        public async Task<IActionResult> BarcodeTagSettings()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access barcode / tag settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = await LoadBarcodeTagSettingsAsync(tenantName, storeCode);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BarcodeTagSettings(BarcodeTagSettingsViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access barcode / tag settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);
            model.TagPrefix = model.TagPrefix?.Trim();
            model.Notes = model.Notes?.Trim();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!ValidateBarcodeTagSettings(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveBarcodeTagSettings", model, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Barcode / tag settings saved successfully."
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save barcode / tag settings.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(BarcodeTagSettings));
        }

        private async Task<BarcodeTagSettingsViewModel> LoadBarcodeTagSettingsAsync(string tenantName, string storeCode)
        {
            BarcodeTagSettingsViewModel model;
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<BarcodeTagSettingsViewModel>("/GetBarcodeTagSettings", parameters, Method.GET);
                model = response ?? BuildDefaultBarcodeTagSettings(tenantName, storeCode);
            }
            catch
            {
                model = BuildDefaultBarcodeTagSettings(tenantName, storeCode);
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);

            if (model.NextTagNumber < 1)
            {
                model.NextTagNumber = 1;
            }

            if (model.TagNumberPadding < 1 || model.TagNumberPadding > 12)
            {
                model.TagNumberPadding = 4;
            }

            if (string.IsNullOrWhiteSpace(model.TagPrefix))
            {
                model.TagPrefix = BuildStoreTagPrefix(model.CompanyName, storeCode);
            }

            return model;
        }

        private static BarcodeTagSettingsViewModel BuildDefaultBarcodeTagSettings(string tenantName, string storeCode)
            => new BarcodeTagSettingsViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableTagging = true,
                TagPrefix = null,
                NextTagNumber = 1,
                TagNumberPadding = 4,
                ResetTagNumberYearly = false
            };

        private static string BuildStoreTagPrefix(string companyName, string storeCode)
        {
            static string Shorten(string value, int maxLength)
            {
                var cleaned = new string((value ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                    .ToUpperInvariant();

                return cleaned.Length > maxLength ? cleaned.Substring(0, maxLength) : cleaned;
            }

            var companyPart = Shorten(companyName, 5);
            var storePart = Shorten(storeCode, 6);

            if (companyPart.Length == 0 && storePart.Length == 0)
            {
                return "TAG";
            }

            if (companyPart.Length == 0)
            {
                return storePart;
            }

            if (storePart.Length == 0)
            {
                return companyPart;
            }

            return companyPart + "-" + storePart;
        }

        private static bool ValidateBarcodeTagSettings(BarcodeTagSettingsViewModel model, out string validationError)
        {
            validationError = null;

            if (!string.IsNullOrWhiteSpace(model.TagPrefix)
                && !model.TagPrefix.Trim().All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '/'))
            {
                validationError = "Tag prefix can contain only letters, numbers, hyphen and slash.";
                return false;
            }

            if (model.NextTagNumber < 1)
            {
                validationError = "Next tag number must be 1 or greater.";
                return false;
            }

            if (model.TagNumberPadding < 1 || model.TagNumberPadding > 12)
            {
                validationError = "Tag number padding must be between 1 and 12.";
                return false;
            }

            return true;
        }

        private static string BuildPrintableTagNumber(string tagPrefix, int nextTagNumber, int tagNumberPadding)
        {
            var safeNumber = nextTagNumber < 1 ? 1 : nextTagNumber;
            var safePadding = tagNumberPadding < 1 || tagNumberPadding > 12 ? 4 : tagNumberPadding;
            var formattedNumber = safeNumber.ToString().PadLeft(safePadding, '0');
            var prefix = (tagPrefix ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(prefix)
                ? formattedNumber
                : prefix + formattedNumber;
        }

        private static string BuildOrderTagNumber(string orderNo, int pieceIndex)
        {
            var normalizedOrderNo = (orderNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedOrderNo))
            {
                normalizedOrderNo = "ORD";
            }

            var safePieceIndex = pieceIndex < 1 ? 1 : pieceIndex;
            return normalizedOrderNo + "-P" + safePieceIndex.ToString().PadLeft(2, '0');
        }

        private Task EnsureOrderTagsFromSettingsAsync(OrderCompletionContextDto context)
        {
            if (context == null || context.Items == null || context.Items.Count == 0)
            {
                return Task.CompletedTask;
            }

            // Printing must never create or mutate tag values.
            return Task.CompletedTask;
        }

        [HttpGet]
        public IActionResult PickupDeliverySettings()
            => AdminOnlyView("Pickup & Delivery Settings", "Configure pickup windows, delivery slots, and dispatch behavior.");

        [HttpGet]
        public async Task<IActionResult> WorkflowStatusSettings()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access workflow status settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = await LoadWorkflowStatusSettingsAsync(tenantName, storeCode);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WorkflowStatusSettings(WorkflowStatusSettingsViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access workflow status settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);
            model.Statuses = (model.Statuses ?? new List<WorkflowStatusItemViewModel>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StatusName))
                .ToList();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!ValidateWorkflowStatusSettings(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            var sortOrder = 1;
            foreach (var status in model.Statuses.OrderBy(x => x.SortOrder).ToList())
            {
                status.StatusName = status.StatusName.Trim();
                status.SortOrder = sortOrder++;
            }

            model.Statuses = model.Statuses.OrderBy(x => x.SortOrder).ToList();

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveWorkflowStatusSettings", model, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Workflow status settings saved successfully."
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save workflow status settings.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(WorkflowStatusSettings));
        }

        /// <summary>
        /// Loads the saved workflow statuses for the logged in store, or the standard
        /// laundry lifecycle when the store has not configured its own list yet.
        /// </summary>
        private async Task<WorkflowStatusSettingsViewModel> LoadWorkflowStatusSettingsAsync(string tenantName, string storeCode)
        {
            WorkflowStatusSettingsViewModel model = null;

            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                model = await _clientApi.SendRequestAsync<WorkflowStatusSettingsViewModel>("/GetWorkflowStatusSettings", parameters, Method.GET);
            }
            catch
            {
                model = null;
            }

            model ??= new WorkflowStatusSettingsViewModel();

            if (model.Statuses == null || model.Statuses.Count == 0)
            {
                model.Statuses = WorkflowStatusSettingsViewModel.GetSeedStatuses();
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);
            model.Statuses = model.Statuses.OrderBy(x => x.SortOrder).ToList();

            return model;
        }

        private static bool ValidateWorkflowStatusSettings(WorkflowStatusSettingsViewModel model, out string validationError)
        {
            validationError = null;

            if (model.Statuses.Count == 0)
            {
                validationError = "Please configure at least one workflow status.";
                return false;
            }

            var duplicate = model.Statuses
                .GroupBy(x => x.StatusName.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
            {
                validationError = $"Duplicate status name '{duplicate.Key}'. Each status name must be unique.";
                return false;
            }

            if (!model.Statuses.Any(x => x.IsActive))
            {
                validationError = "At least one workflow status must be active.";
                return false;
            }

            var defaultCount = model.Statuses.Count(x => x.IsDefault && x.IsActive);
            if (defaultCount == 0)
            {
                validationError = "Please mark one active status as the default for new orders.";
                return false;
            }

            if (defaultCount > 1)
            {
                validationError = "Only one status can be the default for new orders.";
                return false;
            }

            return true;
        }

        [HttpGet]
        public IActionResult NotificationSettings()
            => AdminOnlyView("Notifications Settings", "Configure SMS/email templates and event-based notification rules.");

        [HttpGet]
        public async Task<IActionResult> PaymentSettings()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access payment settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = await LoadPaymentSettingsAsync(tenantName, storeCode);
            return View(model);
        }

        private async Task<Models.Admin.PaymentSettingsViewModel> LoadPaymentSettingsAsync(string tenantName, string storeCode)
        {
            Models.Admin.PaymentSettingsViewModel model;
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<PaymentSettingsViewModel>("/GetPaymentSettings", parameters, Method.GET);

                if (response == null)
                {
                    model = BuildDefaultPaymentSettings(tenantName, storeCode);
                    await TryPersistDefaultPaymentSettingsAsync(model);
                }
                else
                {
                    model = response;
                }
            }
            catch
            {
                model = BuildDefaultPaymentSettings(tenantName, storeCode);
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            return model;
        }

        private async Task TryPersistDefaultPaymentSettingsAsync(PaymentSettingsViewModel model)
        {
            try
            {
                await _clientApi.SendRequestAsync<CustomerIOResponse>("/SavePaymentSettings", model, Method.POST);
            }
            catch
            {
                // Best effort bootstrap only. Order flow should continue with safe in-memory defaults.
            }
        }

        private static PaymentSettingsViewModel BuildDefaultPaymentSettings(string tenantName, string storeCode)
            => new PaymentSettingsViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableCash = true,
                EnableUpi = false,
                EnableCard = false,
                EnableWallet = false,
                DefaultPaymentMode = "Cash",
                AllowPartialPayment = true,
                AllowCredit = false,
                CreditLimitAmount = 0,
                RoundOffPayableAmount = false,
                Notes = string.Empty
            };

        // Quick-fix stub: validate payment settings. Returns true when valid; outputs error message otherwise.
        private bool ValidatePaymentSettings(Models.Admin.PaymentSettingsViewModel model, out string validationError)
        {
            validationError = null;
            if (model == null)
            {
                validationError = "Invalid payment settings.";
                return false;
            }

            // Default payment mode must be one of the enabled modes
            var enabled = GetEnabledPaymentModes(model);
            if (!enabled.Contains(model.DefaultPaymentMode ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                validationError = "Default payment mode must be one of the enabled payment modes.";
                return false;
            }

            if (model.AllowCredit && model.CreditLimitAmount < 0)
            {
                validationError = "Credit limit must be 0 or greater.";
                return false;
            }

            return true;
        }

        // Quick-fix helper: derive enabled payment modes from settings
        private List<string> GetEnabledPaymentModes(Models.Admin.PaymentSettingsViewModel model)
        {
            var list = new List<string>();
            if (model == null) return list;
            if (model.EnableCash) list.Add("Cash");
            if (model.EnableUpi) list.Add("UPI");
            if (model.EnableCard) list.Add("Card");
            if (model.EnableWallet) list.Add("Wallet");
            return list;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PaymentSettings(PaymentSettingsViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access payment settings."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!ValidatePaymentSettings(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            if (!model.AllowCredit)
            {
                model.CreditLimitAmount = 0;
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SavePaymentSettings", model, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Payment settings saved successfully."
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save payment settings.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(PaymentSettings));
        }

        [HttpGet]
        public IActionResult StoreActivation()
        {
            if (!IsSuperAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only super admins can manage store activation."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = superAdminName;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetTenantStoreStatuses()
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can view store activation.", stores = new List<object>() });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again.", stores = new List<object>() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetAllStoreStatuses", new Dictionary<string, string>(), Method.GET);

                var statusesToken = response?["StoreStatuses"] ?? response?["storeStatuses"] ?? new JArray();
                var statusesArray = statusesToken as JArray ?? statusesToken?["$values"] as JArray ?? new JArray();

                var stores = statusesArray
                    .Select(x => new
                    {
                        storeCode = (x?["StoreCode"] ?? x?["storeCode"])?.ToString(),
                        isActive = (bool?)(x?["IsActive"] ?? x?["isActive"]) ?? false,
                        activatedBy = (x?["ActivatedBy"] ?? x?["activatedBy"])?.ToString(),
                        activatedDate = (x?["ActivatedDate"] ?? x?["activatedDate"])?.ToString(),
                        tenantName = (x?["TenantName"] ?? x?["tenantName"])?.ToString(),
                        tenantEmail = (x?["TenantEmail"] ?? x?["tenantEmail"])?.ToString(),
                        currencyCode = (x?["CurrencyCode"] ?? x?["currencyCode"])?.ToString(),
                        currencySymbol = (x?["CurrencySymbol"] ?? x?["currencySymbol"])?.ToString(),
                        currencyLocale = (x?["CurrencyLocale"] ?? x?["currencyLocale"])?.ToString(),
                        currencyDecimalDigits = x?["CurrencyDecimalDigits"] != null ? (int?)x["CurrencyDecimalDigits"].ToObject<int>() : (x?["currencyDecimalDigits"] != null ? (int?)x["currencyDecimalDigits"].ToObject<int>() : null)
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.storeCode))
                    .ToList();

                return Json(new { success = true, stores });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load store activation statuses."),
                    stores = new List<object>()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetStoreActiveStatus(string storeCode, bool isActive)
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can change store activation." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Store code is required." });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/SetStoreActiveStatusByStoreCode", new Dictionary<string, string>
                {
                    { "storeCode", storeCode.Trim() },
                    { "isActive", isActive.ToString().ToLowerInvariant() },
                    { "activatedBy", tenantName }
                }, Method.POST);

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);

                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess ? "Store status updated successfully." : "Unable to update store status.")
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update store status.")
                });
            }
        }

        [HttpGet]
        public IActionResult TenantApproval()
        {
            if (!IsSuperAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only super admin users can approve tenants."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            // Super admins are global accounts without a store, so only the tenant name is required.
            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = superAdminName;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetTenantApprovals()
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can view tenant approvals.", tenants = new List<object>() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetAllTenantApprovals", new Dictionary<string, string>(), Method.GET);

                var approvalsToken = response?["TenantApprovals"] ?? response?["tenantApprovals"] ?? new JArray();
                var approvalsArray = approvalsToken as JArray ?? approvalsToken?["$values"] as JArray ?? new JArray();

                var tenants = approvalsArray
                    .Select(x => new
                    {
                        tenantId = (x?["TenantId"] ?? x?["tenantId"])?.ToString(),
                        tenantName = (x?["TenantName"] ?? x?["tenantName"])?.ToString(),
                        email = (x?["Email"] ?? x?["email"])?.ToString(),
                        phoneNumber = (x?["PhoneNumber"] ?? x?["phoneNumber"])?.ToString(),
                        city = (x?["City"] ?? x?["city"])?.ToString(),
                        country = (x?["Country"] ?? x?["country"])?.ToString(),
                        approvalStatus = (x?["ApprovalStatus"] ?? x?["approvalStatus"])?.ToString(),
                        approvalReason = (x?["ApprovalReason"] ?? x?["approvalReason"])?.ToString(),
                        approvedBy = (x?["ApprovedBy"] ?? x?["approvedBy"])?.ToString(),
                        approvalDate = (x?["ApprovalDate"] ?? x?["approvalDate"])?.ToString(),
                        planCode = (x?["PlanCode"] ?? x?["planCode"])?.ToString(),
                        subscriptionStatus = (x?["SubscriptionStatus"] ?? x?["subscriptionStatus"])?.ToString(),
                        trialEndUtc = (x?["TrialEndUtc"] ?? x?["trialEndUtc"])?.ToString(),
                        storeCount = (int?)(x?["StoreCount"] ?? x?["storeCount"]) ?? 0
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.tenantId))
                    .ToList();

                return Json(new { success = true, tenants });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load tenant approvals."),
                    tenants = new List<object>()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetTenantApprovalStatus(string tenantId, string approvalStatus, string reason)
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can approve tenants." });
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Json(new { success = false, message = "Tenant is required." });
            }

            if (string.Equals(approvalStatus, "Rejected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(reason))
            {
                return Json(new { success = false, message = "A reason is required when rejecting a tenant." });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/SetTenantApprovalStatus", new Dictionary<string, string>
                {
                    { "tenantId", tenantId.Trim() },
                    { "approvalStatus", (approvalStatus ?? string.Empty).Trim() },
                    { "reason", (reason ?? string.Empty).Trim() },
                    { "actionedBy", superAdminName }
                }, Method.POST);

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);

                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess ? "Tenant approval updated successfully." : "Unable to update tenant approval.")
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update tenant approval.")
                });
            }
        }

        [HttpGet]
        public IActionResult SubscriptionPlanManagement()
        {
            if (!IsSuperAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only super admin users can manage subscription plans."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = superAdminName;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetSubscriptionPlans()
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can view subscription plans.", plans = new List<object>() });
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again.", plans = new List<object>() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetSubscriptionPlans", new Dictionary<string, string>(), Method.GET);
                var plansToken = response?["Plans"] ?? response?["plans"] ?? new JArray();
                var plansArray = plansToken as JArray ?? plansToken?["$values"] as JArray ?? new JArray();

                var plans = plansArray
                    .Select(x => new
                    {
                        planCode = (x?["PlanCode"] ?? x?["planCode"])?.ToString(),
                        billingCycle = (x?["BillingCycle"] ?? x?["billingCycle"])?.ToString(),
                        amount = (decimal?)(x?["Amount"] ?? x?["amount"]) ?? 0m,
                        currency = (x?["Currency"] ?? x?["currency"])?.ToString() ?? "INR",
                        isActive = (bool?)(x?["IsActive"] ?? x?["isActive"]) ?? false,
                        modifiedDateUtc = (x?["ModifiedDateUtc"] ?? x?["modifiedDateUtc"])?.ToString(),
                        createdDateUtc = (x?["CreatedDateUtc"] ?? x?["createdDateUtc"])?.ToString(),
                        updatedBy = (x?["UpdatedBy"] ?? x?["updatedBy"])?.ToString()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.planCode) && !string.IsNullOrWhiteSpace(x.billingCycle))
                    .OrderBy(x => x.planCode)
                    .ThenBy(x => x.billingCycle)
                    .ToList();

                return Json(new { success = true, plans });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load subscription plans."),
                    plans = new List<object>()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> UpdateSubscriptionPlanPrice(string planCode, string billingCycle, decimal amount)
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can update subscription plan price." });
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(planCode) || string.IsNullOrWhiteSpace(billingCycle))
            {
                return Json(new { success = false, message = "Plan and billing cycle are required." });
            }

            if (amount <= 0)
            {
                return Json(new { success = false, message = "Amount must be greater than zero." });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/UpdateSubscriptionPlanPrice", new
                {
                    planCode = planCode.Trim(),
                    billingCycle = billingCycle.Trim(),
                    amount
                }, Method.POST);

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);
                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess ? "Plan price updated successfully." : "Unable to update plan price.")
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update plan price.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetSubscriptionPlanActiveStatus(string planCode, string billingCycle, bool isActive)
        {
            if (!IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Only super admin users can change subscription plan status." });
            }

            var superAdminName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(superAdminName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(planCode) || string.IsNullOrWhiteSpace(billingCycle))
            {
                return Json(new { success = false, message = "Plan and billing cycle are required." });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/SetSubscriptionPlanActiveStatus", new
                {
                    planCode = planCode.Trim(),
                    billingCycle = billingCycle.Trim(),
                    isActive
                }, Method.POST);

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);
                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess ? "Subscription plan status updated successfully." : "Unable to update subscription plan status.")
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update subscription plan status.")
                });
            }
        }

        [HttpGet]
        public IActionResult TenantSubscriptionPlans()
        {
            if (IsSuperAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Tenant subscription checkout is available only for tenant users."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["UserRole"] = HttpContext.Session.GetString("UserRole") ?? "StoreUser";
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetTenantSubscriptionOverview()
        {
            if (IsSuperAdminUser())
            {
                return Json(new { success = false, message = "Super admin users do not have tenant subscription checkout.", overview = new object() });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again.", overview = new object() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetTenantSubscriptionOverview", new Dictionary<string, string>(), Method.GET);
                var overview = new TenantSubscriptionOverviewDto
                {
                    TenantId = (response?["TenantId"] ?? response?["tenantId"])?.ToString(),
                    PlanCode = (response?["PlanCode"] ?? response?["planCode"])?.ToString(),
                    BillingCycle = (response?["BillingCycle"] ?? response?["billingCycle"])?.ToString(),
                    SubscriptionStatus = (response?["SubscriptionStatus"] ?? response?["subscriptionStatus"])?.ToString(),
                    TrialStartUtc = TryParseDateTime(response?["TrialStartUtc"] ?? response?["trialStartUtc"]),
                    TrialEndUtc = TryParseDateTime(response?["TrialEndUtc"] ?? response?["trialEndUtc"]),
                    SubscriptionStartUtc = TryParseDateTime(response?["SubscriptionStartUtc"] ?? response?["subscriptionStartUtc"]),
                    SubscriptionEndUtc = TryParseDateTime(response?["SubscriptionEndUtc"] ?? response?["subscriptionEndUtc"]),
                    GraceEndUtc = TryParseDateTime(response?["GraceEndUtc"] ?? response?["graceEndUtc"]),
                    NextRenewalUtc = TryParseDateTime(response?["NextRenewalUtc"] ?? response?["nextRenewalUtc"])
                };

                var plansToken = response?["AvailablePlans"] ?? response?["availablePlans"] ?? new JArray();
                var plansArray = plansToken as JArray ?? plansToken?["$values"] as JArray ?? new JArray();
                overview.AvailablePlans = plansArray
                    .Select(x => new TenantSubscriptionPlanOptionDto
                    {
                        PlanCode = (x?["PlanCode"] ?? x?["planCode"])?.ToString(),
                        BillingCycle = (x?["BillingCycle"] ?? x?["billingCycle"])?.ToString(),
                        Amount = (decimal?)(x?["Amount"] ?? x?["amount"]) ?? 0m,
                        Currency = (x?["Currency"] ?? x?["currency"])?.ToString() ?? "INR",
                        IsActive = (bool?)(x?["IsActive"] ?? x?["isActive"]) ?? false
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.PlanCode) && !string.IsNullOrWhiteSpace(x.BillingCycle) && x.IsActive)
                    .OrderBy(x => x.PlanCode)
                    .ThenBy(x => x.BillingCycle)
                    .ToList();

                var historyToken = response?["PaymentHistory"] ?? response?["paymentHistory"] ?? new JArray();
                var historyArray = historyToken as JArray ?? historyToken?["$values"] as JArray ?? new JArray();
                overview.PaymentHistory = historyArray
                    .Select(x => new TenantSubscriptionPaymentHistoryItemDto
                    {
                        PaymentOrderId = (int?)(x?["PaymentOrderId"] ?? x?["paymentOrderId"]) ?? 0,
                        PlanCode = (x?["PlanCode"] ?? x?["planCode"])?.ToString(),
                        BillingCycle = (x?["BillingCycle"] ?? x?["billingCycle"])?.ToString(),
                        Amount = (decimal?)(x?["Amount"] ?? x?["amount"]) ?? 0m,
                        Currency = (x?["Currency"] ?? x?["currency"])?.ToString() ?? "INR",
                        OrderStatus = (x?["OrderStatus"] ?? x?["orderStatus"])?.ToString(),
                        GatewayPaymentId = (x?["GatewayPaymentId"] ?? x?["gatewayPaymentId"])?.ToString(),
                        FailureReason = (x?["FailureReason"] ?? x?["failureReason"])?.ToString(),
                        CreatedUtc = TryParseDateTime(x?["CreatedUtc"] ?? x?["createdUtc"]),
                        ModifiedUtc = TryParseDateTime(x?["ModifiedUtc"] ?? x?["modifiedUtc"])
                    })
                    .OrderByDescending(x => x.CreatedUtc)
                    .ToList();

                return Json(new { success = true, overview });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load tenant subscription details."),
                    overview = new object()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> CreateTenantSubscriptionPaymentOrder(string planCode, string billingCycle)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Only tenant admin users can subscribe or renew." });
            }

            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("TenantName")))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(planCode) || string.IsNullOrWhiteSpace(billingCycle))
            {
                return Json(new { success = false, message = "Plan and billing cycle are required." });
            }

            var request = new CreateTenantSubscriptionPaymentOrderRequestDto
            {
                PlanCode = planCode.Trim(),
                BillingCycle = billingCycle.Trim(),
                ClientRequestId = "sub_" + Guid.NewGuid().ToString("N")
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<CreateTenantSubscriptionPaymentOrderResponseDto>("/CreateTenantSubscriptionPaymentOrder", request, Method.POST);
                if (response == null || string.IsNullOrWhiteSpace(response.RazorpayOrderId) || string.IsNullOrWhiteSpace(response.RazorpayKeyId))
                {
                    return Json(new { success = false, message = "Unable to create subscription checkout order." });
                }

                return Json(new
                {
                    success = true,
                    order = response
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to create subscription checkout order.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> VerifyTenantSubscriptionCheckoutPayment(int paymentOrderId, string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Only tenant admin users can verify subscription payments." });
            }

            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("TenantName")))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (paymentOrderId <= 0
                || string.IsNullOrWhiteSpace(razorpayOrderId)
                || string.IsNullOrWhiteSpace(razorpayPaymentId)
                || string.IsNullOrWhiteSpace(razorpaySignature))
            {
                return Json(new { success = false, message = "Invalid payment verification payload." });
            }

            var request = new VerifyTenantSubscriptionCheckoutPaymentRequestDto
            {
                PaymentOrderId = paymentOrderId,
                RazorpayOrderId = razorpayOrderId.Trim(),
                RazorpayPaymentId = razorpayPaymentId.Trim(),
                RazorpaySignature = razorpaySignature.Trim()
            };

            try
            {
                var response = await _loginApi.SendRequestAsync<VerifyTenantSubscriptionCheckoutPaymentResponseDto>("/VerifyTenantSubscriptionCheckoutPayment", request, Method.POST);
                var isSuccess = response != null && response.Success;
                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess ? "Subscription payment verified successfully." : "Unable to verify subscription payment.")
                        : response.Message,
                    result = response
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to verify subscription payment.")
                });
            }
        }

        private static DateTime? TryParseDateTime(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Date)
            {
                var tokenValue = token.Value<DateTime?>();
                if (!tokenValue.HasValue)
                {
                    return null;
                }

                var dt = tokenValue.Value;
                if (dt.Kind == DateTimeKind.Unspecified)
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }

                return dt.ToUniversalTime();
            }

            var raw = token.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var parsedOffset))
            {
                return parsedOffset.UtcDateTime;
            }

            if (DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : parsed.ToUniversalTime();
            }

            return null;
        }

        [HttpGet]
        public async Task<IActionResult> NewStoreCreation()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can create stores."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = storeCode;

            var (existingStores, loadErrorMessage) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantName, storeCode);
            ViewData["ExistingStores"] = existingStores.Select(x => x.StoreCode).ToList();
            ViewData["ExistingStoreStatuses"] = existingStores;
            ViewData["ExistingStoresLoadError"] = loadErrorMessage;

            return View();
        }

        [HttpPost]
        public async Task<JsonResult> CreateNewStore(string newStoreCode)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can create stores." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            newStoreCode = newStoreCode?.Trim();
            if (string.IsNullOrWhiteSpace(newStoreCode))
            {
                return Json(new { success = false, message = "Please enter store code." });
            }

            try
            {
                var response = await TryCreateTenantStoreAsync(tenantName, newStoreCode);

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);

                if (!isSuccess)
                {
                    return Json(new
                    {
                        success = false,
                        message = string.IsNullOrWhiteSpace(response?.Message)
                            ? "Unable to create store."
                            : response.Message
                    });
                }

                var allowedStores = (HttpContext.Session.GetString(AllowedStoresSessionKey) ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!allowedStores.Contains(newStoreCode, StringComparer.OrdinalIgnoreCase))
                {
                    allowedStores.Add(newStoreCode);
                }

                HttpContext.Session.SetString(AllowedStoresSessionKey, string.Join(",", allowedStores));

                // Invalidate currency cache for this tenant/store so dashboard reflects new defaults immediately
                try
                {
                    var tenant = tenantName ?? string.Empty;
                    var store = newStoreCode ?? string.Empty;
                    try
                    {
                        var versionKey = LMSWebUI.Services.CurrencyCacheKeys.TenantVersionKey(tenant);
                        _memoryCache.Remove(versionKey);
                    }
                    catch { }

                    try
                    {
                        var specificKey = LMSWebUI.Services.CurrencyCacheKeys.CurrencyKey(_memoryCache, tenant, store);
                        _memoryCache.Remove(specificKey);
                    }
                    catch { }
                }
                catch
                {
                    // ignore cache eviction failures
                }

                return Json(new
                {
                    success = true,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Store created successfully."
                        : response.Message,
                    storeCode = newStoreCode,
                    allowedStores
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to create store.", "Store code already exists.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetNewStoreCreationStoreActiveStatus(string storeCode, bool isActive)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can change store status." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            var normalizedStoreCode = storeCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedStoreCode))
            {
                return Json(new { success = false, message = "Store code is required." });
            }

            try
            {
                var (tenantStores, loadErrorMessage) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantName, currentStoreCode);
                if (!string.IsNullOrWhiteSpace(loadErrorMessage) && tenantStores.Count == 0)
                {
                    return Json(new { success = false, message = loadErrorMessage });
                }

                var targetStore = tenantStores.FirstOrDefault(x => string.Equals(x.StoreCode, normalizedStoreCode, StringComparison.OrdinalIgnoreCase));
                if (targetStore == null)
                {
                    return Json(new { success = false, message = "Invalid store selected." });
                }

                LoginIoResponse response = null;
                try
                {
                    response = await _loginApi.SendRequestAsync<LoginIoResponse>("/SetStoreActiveStatusByStoreCode", new Dictionary<string, string>
                    {
                        { "storeCode", normalizedStoreCode },
                        { "isActive", isActive ? "true" : "false" },
                        { "activatedBy", tenantName }
                    }, Method.POST);
                }
                catch (Exception ex)
                {
                    var message = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
                    var shouldFallbackToMaster = ex is ApiUnavailableException
                        || message.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("404", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase);

                    if (!shouldFallbackToMaster)
                    {
                        throw;
                    }

                    response = await _clientApi.SendRequestAsync<LoginIoResponse>("/SetStoreActiveStatusByStoreCode", new Dictionary<string, string>
                    {
                        { "storeCode", normalizedStoreCode },
                        { "isActive", isActive ? "true" : "false" },
                        { "activatedBy", tenantName }
                    }, Method.POST);
                }

                var isSuccess = response != null && string.Equals(response.StatusCode, "200", StringComparison.OrdinalIgnoreCase);
                return Json(new
                {
                    success = isSuccess,
                    message = string.IsNullOrWhiteSpace(response?.Message)
                        ? (isSuccess
                            ? (isActive ? "Store enabled successfully." : "Store disabled successfully.")
                            : "Unable to update store status.")
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update store status.")
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetNewStoreCreationStoreStatuses()
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can view store status.", stores = new List<object>() });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again.", stores = new List<object>() });
            }

            var (stores, loadErrorMessage) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantName, currentStoreCode);
            if (!string.IsNullOrWhiteSpace(loadErrorMessage) && stores.Count == 0)
            {
                return Json(new { success = false, message = loadErrorMessage, stores = new List<object>() });
            }

            return Json(new
            {
                success = true,
                stores = stores.Select(x => new
                {
                    storeCode = x.StoreCode,
                    isActive = x.IsActive,
                    currencyCode = x.CurrencyCode,
                    currencySymbol = x.CurrencySymbol,
                    currencyLocale = x.CurrencyLocale,
                    currencyDecimalDigits = x.CurrencyDecimalDigits
                })
            });
        }

        private sealed class TenantStoreStatusViewModel
        {
            public string StoreCode { get; set; }
            public bool IsActive { get; set; }
            public string ActivatedBy { get; set; }
            public string ActivatedDate { get; set; }
            public string TenantName { get; set; }
            public string TenantEmail { get; set; }

            // Currency fields (may be null if not provided by API)
            public string CurrencyCode { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyLocale { get; set; }
            public int? CurrencyDecimalDigits { get; set; }
        }

        private async Task<(List<TenantStoreStatusViewModel> Stores, string ErrorMessage)> GetTenantStoreStatusesForNewStoreCreationAsync(string tenantName, string currentStoreCode)
        {
            var stores = new List<string>();
            Exception lastException = null;

            JObject successResponse = null;

            if (!string.IsNullOrWhiteSpace(tenantName))
            {
                var parameters = new Dictionary<string, string>
                {
                    { "eMail", tenantName }
                };

                // Try to load full tenant store details which may include per-store configuration
                // (StoreConfigurations) so the UI can render currency without extra API calls.

                try
                {
                    JObject response = null;
                    try
                    {
                        response = await _loginApi.SendRequestAsync<JObject>("/GetTenantStoreDetails", parameters, Method.GET);
                    }
                    catch
                    {
                        try
                        {
                            response = await _loginApi.SendRequestAsync<JObject>("/TenantprofileDetails", parameters, Method.GET);
                        }
                        catch
                        {
                            // fallback to master api client
                            try
                            {
                                response = await _clientApi.SendRequestAsync<JObject>("/GetTenantStoreDetails", parameters, Method.GET);
                            }
                            catch
                            {
                                response = await _clientApi.SendRequestAsync<JObject>("/TenantprofileDetails", parameters, Method.GET);
                            }
                        }
                    }

                    if (response != null)
                    {
                        successResponse = response;
                        var storecodesToken = response?["Storecodes"] ?? response?["storeCodes"] ?? response?["storecodes"];
                        var storecodesArray = storecodesToken as JArray ?? (storecodesToken != null ? JArray.FromObject(storecodesToken) : null);
                        if (storecodesArray != null)
                        {
                            foreach (var sc in storecodesArray)
                            {
                                var code = sc?.ToString();
                                if (!string.IsNullOrWhiteSpace(code)) stores.Add(code);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
                // If API returned StoreConfigurations, we'll parse them later when building the final view models
            }

            if (!string.IsNullOrWhiteSpace(currentStoreCode))
            {
                stores.Add(currentStoreCode);
            }

            var normalizedStores = stores
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var statusLookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(tenantName))
            {
                try
                {
                    // Prefer statuses delivered as part of the tenant store details response (already fetched above)
                    if (successResponse != null)
                    {
                        var statusesToken = successResponse?["StoreStatuses"] ?? successResponse?["storeStatuses"] ?? successResponse?["StoreStatuses"];
                        var statusesArray = statusesToken as JArray ?? (statusesToken != null ? JArray.FromObject(statusesToken) : null);
                        if (statusesArray != null)
                        {
                            foreach (var item in statusesArray)
                            {
                                var itemStoreCode = (item?["StoreCode"] ?? item?["storeCode"])?.ToString()?.Trim();
                                if (string.IsNullOrWhiteSpace(itemStoreCode)) continue;

                                var itemTenantName = (item?["TenantName"] ?? item?["tenantName"])?.ToString()?.Trim();
                                var itemTenantEmail = (item?["TenantEmail"] ?? item?["tenantEmail"])?.ToString()?.Trim();
                                var belongsToTenant = string.Equals(itemTenantName, tenantName, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(itemTenantEmail, tenantName, StringComparison.OrdinalIgnoreCase);

                                if (!belongsToTenant) continue;

                                // Support multiple status representations returned by various API versions.
                                bool isActive = false;
                                var activeToken = item?["IsActive"] ?? item?["isActive"];
                                if (activeToken != null)
                                {
                                    try
                                    {
                                        if (activeToken.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
                                            isActive = activeToken.Value<bool>();
                                        else
                                        {
                                            var sval = activeToken.ToString()?.Trim();
                                            isActive = !string.IsNullOrWhiteSpace(sval) && (
                                                string.Equals(sval, "1", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(sval, "true", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(sval, "yes", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(sval, "active", StringComparison.OrdinalIgnoreCase));
                                        }
                                    }
                                    catch
                                    {
                                        isActive = false;
                                    }
                                }
                                else
                                {
                                    // Some endpoints may return a textual Status field (e.g. "Active"/"InActive").
                                    var statusToken = item?["Status"] ?? item?["status"];
                                    var s = statusToken?.ToString()?.Trim();
                                    isActive = !string.IsNullOrWhiteSpace(s) && string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase);
                                }

                                statusLookup[itemStoreCode] = isActive;
                            }
                        }
                    }
                    else
                    {
                        JObject allStatuses = null;
                        try
                        {
                            allStatuses = await _loginApi.SendRequestAsync<JObject>("/GetAllStoreStatuses", new Dictionary<string, string>(), Method.GET);
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            var message = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
                            var shouldFallbackToMaster = ex is ApiUnavailableException
                                || message.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                                || message.Contains("404", StringComparison.OrdinalIgnoreCase)
                                || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                                || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase);

                            if (shouldFallbackToMaster)
                            {
                                allStatuses = await _clientApi.SendRequestAsync<JObject>("/GetAllStoreStatuses", new Dictionary<string, string>(), Method.GET);
                            }
                            else
                            {
                                throw;
                            }
                        }

                        var statusesToken = allStatuses?["StoreStatuses"] ?? allStatuses?["storeStatuses"] ?? new JArray();
                        var statusesArray = statusesToken as JArray ?? statusesToken?["$values"] as JArray ?? new JArray();

                        foreach (var item in statusesArray)
                        {
                            var itemStoreCode = (item?["StoreCode"] ?? item?["storeCode"])?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(itemStoreCode)) continue;

                            var itemTenantName = (item?["TenantName"] ?? item?["tenantName"])?.ToString()?.Trim();
                            var itemTenantEmail = (item?["TenantEmail"] ?? item?["tenantEmail"])?.ToString()?.Trim();
                            var belongsToTenant = string.Equals(itemTenantName, tenantName, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(itemTenantEmail, tenantName, StringComparison.OrdinalIgnoreCase);

                            if (!belongsToTenant) continue;

                            // Support multiple status representations (boolean, numeric, or textual)
                            bool isActive = false;
                            var activeToken = item?["IsActive"] ?? item?["isActive"];
                            if (activeToken != null)
                            {
                                try
                                {
                                    if (activeToken.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
                                        isActive = activeToken.Value<bool>();
                                    else
                                    {
                                        var sval = activeToken.ToString()?.Trim();
                                        isActive = !string.IsNullOrWhiteSpace(sval) && (
                                            string.Equals(sval, "1", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(sval, "true", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(sval, "yes", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(sval, "active", StringComparison.OrdinalIgnoreCase));
                                    }
                                }
                                catch
                                {
                                    isActive = false;
                                }
                            }
                            else
                            {
                                var statusToken = item?["Status"] ?? item?["status"];
                                var s = statusToken?.ToString()?.Trim();
                                isActive = !string.IsNullOrWhiteSpace(s) && string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase);
                            }

                            statusLookup[itemStoreCode] = isActive;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            // Build a lookup of per-store configuration returned by GetTenantStoreDetails (if available)
            var cfgLookup = new Dictionary<string, (string code, string symbol, string locale, int? digits)>(StringComparer.OrdinalIgnoreCase);
            if (successResponse != null)
            {
                try
                {
                    var cfgToken = successResponse?["StoreConfigurations"] ?? successResponse?["storeConfigurations"];
                    var cfgArray = cfgToken as JArray ?? (cfgToken != null ? JArray.FromObject(cfgToken) : null);
                    if (cfgArray != null)
                    {
                        foreach (var c in cfgArray)
                        {
                            var sc = (c?["StoreCode"] ?? c?["storeCode"])?.ToString();
                            if (string.IsNullOrWhiteSpace(sc)) continue;
                            var cc = (c?["CurrencyCode"] ?? c?["currencyCode"])?.ToString();
                            var cs = (c?["CurrencySymbol"] ?? c?["currencySymbol"])?.ToString();
                            var cl = (c?["CurrencyLocale"] ?? c?["currencyLocale"])?.ToString();
                            int? cd = null;
                            try { cd = c?["CurrencyDecimalDigits"] != null ? (int?)c["CurrencyDecimalDigits"].ToObject<int>() : (c?["currencyDecimalDigits"] != null ? (int?)c["currencyDecimalDigits"].ToObject<int>() : null); } catch { cd = null; }
                            cfgLookup[sc.Trim()] = (cc, cs, cl, cd);
                        }
                    }
                }
                catch
                {
                    // ignore config parsing errors
                }
            }

            var storesWithStatus = normalizedStores
                .Select(code =>
                {
                    cfgLookup.TryGetValue(code, out var cfg);

                    return new TenantStoreStatusViewModel
                    {
                        StoreCode = code,
                        IsActive = statusLookup.TryGetValue(code, out var isActive) && isActive,
                        CurrencyCode = cfg.code,
                        CurrencySymbol = cfg.symbol,
                        CurrencyLocale = cfg.locale,
                        CurrencyDecimalDigits = cfg.digits
                    };
                })
                .ToList();

            var errorMessage = storesWithStatus.Count == 0 && lastException != null
                ? BuildApiErrorMessage(lastException, "Unable to load existing stores.")
                : string.Empty;

            return (storesWithStatus, errorMessage);
        }

        [HttpGet]
        public async Task<IActionResult> ImportExportPricesCsv()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access this page."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(tenantName, storeCode);
            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Login", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = storeCode;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Integrations()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access integrations."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(tenantName, storeCode);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var model = await LoadIntegrationsAsync(tenantName, storeCode);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveIntegration(Models.Admin.IntegrationProviderViewModel model)
        {
            if (!IsAdminUser())
            {
                return RedirectToAction(nameof(Integrations));
            }

            if (model == null)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Invalid integration payload."
                });
                return RedirectToAction(nameof(Integrations));
            }

            model.TenantName = HttpContext.Session.GetString("TenantName");
            model.StoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(model.TenantName) || string.IsNullOrWhiteSpace(model.StoreCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(model.TenantName, model.StoreCode);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var result = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveTenantIntegrationSettings", model, Method.POST);
            TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
            {
                CssClassName = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
                    ? "alert alert-success"
                    : "alert alert-danger",
                Title = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase) ? "Success." : "Failed.",
                DisplayMessage = result?.Message ?? "Unable to save integration settings."
            });

            return RedirectToAction(nameof(Integrations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleIntegration(string category, string providerKey, bool isEnabled)
        {
            if (!IsAdminUser())
            {
                return RedirectToAction(nameof(Integrations));
            }

            var payload = new
            {
                TenantName = HttpContext.Session.GetString("TenantName"),
                StoreCode = HttpContext.Session.GetString("TenantStore"),
                Category = category,
                ProviderKey = providerKey,
                IsEnabled = isEnabled
            };

            try
            {
                var result = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SetTenantIntegrationEnabled", payload, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
                        ? "alert alert-success"
                        : "alert alert-danger",
                    Title = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase) ? "Success." : "Failed.",
                    DisplayMessage = result?.Message ?? "Unable to update integration status."
                });
            }
            catch (ApiUnavailableException ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to update integration status.")
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to update integration status.")
                });
            }

            return RedirectToAction(nameof(Integrations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestIntegration(string category, string providerKey)
        {
            if (!IsAdminUser())
            {
                return RedirectToAction(nameof(Integrations));
            }

            var payload = new
            {
                TenantName = HttpContext.Session.GetString("TenantName"),
                StoreCode = HttpContext.Session.GetString("TenantStore"),
                Category = category,
                ProviderKey = providerKey
            };

            var result = await _clientApi.SendRequestAsync<CustomerIOResponse>("/TestTenantIntegration", payload, Method.POST);
            TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
            {
                CssClassName = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
                    ? "alert alert-success"
                    : "alert alert-warning",
                Title = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase) ? "Test Success." : "Test Failed.",
                DisplayMessage = result?.Message ?? "Unable to validate integration settings."
            });

            return RedirectToAction(nameof(Integrations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTestEmail(string category, string providerKey, string recipientEmail)
        {
            if (!IsAdminUser())
            {
                return RedirectToAction(nameof(Integrations));
            }

            if (string.IsNullOrWhiteSpace(recipientEmail) || !new EmailAddressAttribute().IsValid(recipientEmail))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-warning",
                    Title = "Test Failed.",
                    DisplayMessage = "Please enter a valid recipient email address."
                });

                return RedirectToAction(nameof(Integrations));
            }

            var payload = new
            {
                TenantName = HttpContext.Session.GetString("TenantName"),
                StoreCode = HttpContext.Session.GetString("TenantStore"),
                Category = category,
                ProviderKey = providerKey,
                RecipientEmail = recipientEmail.Trim()
            };

            try
            {
                var result = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SendTenantIntegrationTestEmail", payload, Method.POST);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
                        ? "alert alert-success"
                        : "alert alert-warning",
                    Title = string.Equals(result?.StatusCode, "200", StringComparison.OrdinalIgnoreCase) ? "Test Success." : "Test Failed.",
                    DisplayMessage = result?.Message ?? "Unable to send test email."
                });
            }
            catch (ApiUnavailableException ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-warning",
                    Title = "Test Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to send test email.")
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-warning",
                    Title = "Test Failed.",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to send test email.")
                });
            }

            return RedirectToAction(nameof(Integrations));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevealWebhookSecret(string category, string providerKey)
        {
            static string GetSafeRevealMessage(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return null;
                }

                var text = candidate.Trim();

                if (text.IndexOf("Integration configuration not found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Integration configuration not found. Save configuration first.";
                }

                if (text.IndexOf("Webhook secret is not configured", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Webhook secret is not configured for this integration.";
                }

                if (text.IndexOf("Webhook secret was not generated by ELS", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Webhook secret was not generated by ELS. Rotate secret to create a managed one-time reveal secret.";
                }

                if (text.IndexOf("Webhook secret has already been revealed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Webhook secret has already been revealed once. Rotate the secret to generate a new one-time secret.";
                }

                if (text.IndexOf("only for Razorpay payment integration", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Razorpay integration validation failed.";
                }

                return null;
            }

            static string TryExtractSafeRevealMessage(Exception ex)
            {
                var rawMessage = ex?.InnerException?.Message ?? ex?.Message;
                if (string.IsNullOrWhiteSpace(rawMessage))
                {
                    return null;
                }

                var safeMessage = GetSafeRevealMessage(rawMessage);
                if (!string.IsNullOrWhiteSpace(safeMessage))
                {
                    return safeMessage;
                }

                try
                {
                    if (!rawMessage.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    var token = JToken.Parse(rawMessage);
                    var parsedMessage = token["Message"]?.ToString()
                                      ?? token["message"]?.ToString()
                                      ?? token["ResultSet"]?["Message"]?.ToString()
                                      ?? token["resultSet"]?["message"]?.ToString();

                    return GetSafeRevealMessage(parsedMessage);
                }
                catch
                {
                    return null;
                }
            }

            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can reveal webhook secrets." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            var payload = new
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? "razorpay" : providerKey.Trim()
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<JObject>("/RevealTenantIntegrationWebhookSecretOnce", payload, Method.POST);

                if (response == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Master API returned no usable JSON response."
                    });
                }

                if (!response.Properties().Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "Master API returned an empty JSON object."
                    });
                }

                var webhookSecret = response?[("webhookSecret")]?.ToString()
                    ?? response?[("WebhookSecret")]?.ToString();
                var responseMessage = response?[("message")]?.ToString()
                    ?? response?[("Message")]?.ToString();
                var responseStatusCode = response?[("statusCode")]?.ToString()
                    ?? response?[("StatusCode")]?.ToString();

                if (!string.IsNullOrWhiteSpace(webhookSecret))
                {
                    return Json(new
                    {
                        success = true,
                        webhookSecret = webhookSecret,
                        message = string.IsNullOrWhiteSpace(responseMessage)
                            ? "Webhook secret revealed once. Copy it now and add it in Razorpay Dashboard webhook configuration."
                            : responseMessage
                    });
                }

                return Json(new
                {
                    success = false,
                    message = GetSafeRevealMessage(responseMessage)
                        ?? $"Unexpected Master API response. StatusCode: {(string.IsNullOrWhiteSpace(responseStatusCode) ? "Unknown" : responseStatusCode)}. MessagePresent: {!string.IsNullOrWhiteSpace(responseMessage)}."
                });
            }
            catch (ApiUnavailableException ex)
            {
                var safeDiagnostic = ex.Message;
                if (!string.IsNullOrWhiteSpace(safeDiagnostic)
                    && safeDiagnostic.StartsWith("Master API JSON diagnostic:", StringComparison.Ordinal))
                {
                    return Json(new
                    {
                        success = false,
                        message = safeDiagnostic
                    });
                }

                var statusCode = ex.StatusCode;
                return Json(new
                {
                    success = false,
                    message = statusCode.HasValue
                        ? $"Master API error: HTTP {statusCode.Value}."
                        : "Unable to reveal webhook secret. Please try again."
                });
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to reveal webhook secret. Please try again."
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RotateWebhookSecret(string category, string providerKey)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can rotate webhook secrets." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            var payload = new
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? "razorpay" : providerKey.Trim()
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/RotateTenantIntegrationWebhookSecret", payload, Method.POST);
                return Json(new
                {
                    success = string.Equals(response?.StatusCode, "200", StringComparison.OrdinalIgnoreCase),
                    message = response?.Message ?? "Unable to rotate webhook secret."
                });
            }
            catch (ApiUnavailableException)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to rotate webhook secret. Please try again."
                });
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to rotate webhook secret. Please try again."
                });
            }
        }

        private async Task<Models.Admin.IntegrationsViewModel> LoadIntegrationsAsync(string tenantName, string storeCode)
        {
            var model = new Models.Admin.IntegrationsViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                Providers = new List<Models.Admin.IntegrationProviderViewModel>()
            };

            try
            {
                var providers = await _clientApi.SendRequestAsync<List<Models.Admin.IntegrationProviderViewModel>>(
                    "/GetTenantIntegrationSettings",
                    BuildScopedParameters(),
                    Method.GET);

                if (providers != null && providers.Count > 0)
                {
                    model.Providers = providers;
                }

                if (model.Providers != null)
                {
                    foreach (var provider in model.Providers)
                    {
                        provider.TenantName = tenantName;
                        provider.StoreCode = storeCode;
                    }
                }
            }
            catch
            {
            }

            EnsureDefaultIntegrationCards(model);
            return model;
        }

        private static void EnsureDefaultIntegrationCards(Models.Admin.IntegrationsViewModel model)
        {
            if (model?.Providers == null)
            {
                return;
            }

            var reordered = new List<Models.Admin.IntegrationProviderViewModel>();

            Models.Admin.IntegrationProviderViewModel existingOrNew(string category, string providerKey, string providerName)
            {
                var existing = model.Providers.FirstOrDefault(x =>
                    string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    return existing;
                }

                return new Models.Admin.IntegrationProviderViewModel
                {
                    TenantName = model.TenantName,
                    StoreCode = model.StoreCode,
                    Category = category,
                    ProviderKey = providerKey,
                    ProviderDisplayName = providerName,
                    IsEnabled = false,
                    IsTestMode = true,
                    LastTestStatus = "NotTested",
                    LastTestMessage = "Not tested yet."
                };
            }

            reordered.Add(existingOrNew("Email", "smtp", "SMTP"));
            reordered.Add(existingOrNew("Other", "generic-other", "Other Connector"));
            reordered.Add(existingOrNew("Payments", "generic-payment", "Payment Gateway"));
            reordered.Add(existingOrNew("SMS", "generic-sms", "SMS Provider"));
            reordered.Add(existingOrNew("WhatsApp", "meta-cloud", "WhatsApp Cloud API"));

            model.Providers = reordered;
        }

        [HttpPost]
        public async Task<JsonResult> CreateStoreUser(string email, string password, string role = "StoreUser", string storeCode = null, List<string> storeCodes = null)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can create store users." });
            }

            var tenantEmail = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Json(new { success = false, message = "Email and password are required." });
            }

            var requestedStoreCodes = (storeCodes ?? new List<string>())
                .Concat(string.IsNullOrWhiteSpace(storeCode) ? Enumerable.Empty<string>() : new[] { storeCode })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requestedStoreCodes.Count == 0)
            {
                return Json(new { success = false, message = "Please select at least one active store." });
            }

            try
            {
                var resolvedStoreCodes = new List<string>();
                foreach (var requestedStoreCode in requestedStoreCodes)
                {
                    if (!TryResolveTargetStore(currentStoreCode, requestedStoreCode, out var resolvedStoreCode, out var validationMessage))
                    {
                        return Json(new { success = false, message = validationMessage });
                    }

                    var (isValidStore, storeValidationMessage) = await ValidateTenantActiveStoreSelectionAsync(tenantEmail, currentStoreCode, resolvedStoreCode);
                    if (!isValidStore)
                    {
                        return Json(new { success = false, message = storeValidationMessage });
                    }

                    if (!resolvedStoreCodes.Contains(resolvedStoreCode, StringComparer.OrdinalIgnoreCase))
                    {
                        resolvedStoreCodes.Add(resolvedStoreCode);
                    }
                }

                var successStores = new List<string>();
                var failedStores = new List<string>();

                foreach (var resolvedStoreCode in resolvedStoreCodes)
                {
                    var request = new
                    {
                        TenantEmail = tenantEmail,
                        StoreCode = resolvedStoreCode,
                        Email = email.Trim(),
                        Password = password,
                        Role = string.IsNullOrWhiteSpace(role) ? "StoreUser" : role.Trim(),
                        IsActive = true,
                        CreatedBy = tenantEmail
                    };

                    var response = await _loginApi.SendRequestAsync<CustomerIOResponse>("/CreateStoreUser", request, Method.POST);
                    if (response != null && response.StatusCode == "200")
                    {
                        successStores.Add(resolvedStoreCode);
                    }
                    else
                    {
                        failedStores.Add($"{resolvedStoreCode}: {(response?.Message ?? "Unable to create assignment")}");
                    }
                }

                if (successStores.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = failedStores.Count > 0
                            ? string.Join("; ", failedStores)
                            : "Unable to create store user."
                    });
                }

                if (failedStores.Count > 0)
                {
                    return Json(new
                    {
                        success = true,
                        message = $"Store user assigned to {string.Join(", ", successStores)}. Some assignments failed: {string.Join("; ", failedStores)}"
                    });
                }

                return Json(new
                {
                    success = true,
                    message = $"Store user created successfully and assigned to {string.Join(", ", successStores)}."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to create store user.")
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetLiveCounterDashboardStats()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again."
                });
            }

            try
            {
                var customersResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomers", BuildScopedParameters(("eMail", string.Empty)), RestSharp.Method.GET);

                var customerNames = (customersResponse?.CustomerNames ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var todayOrders = 0;
                var pendingPaymentCount = 0;
                decimal pendingPaymentAmount = 0;
                decimal advanceUsedToday = 0;
                decimal amountCollectedToday = 0;
                decimal cashCollectedToday = 0;
                decimal upiCollectedToday = 0;
                decimal cardCollectedToday = 0;
                var customersWithOrders = 0;
                var readyForDeliveryCount = 0;
                var pendingDeliveryCount = 0;
                var pendingProcessingCount = 0;
                var paymentSummaryAvailable = customersResponse != null
                    && (string.IsNullOrWhiteSpace(customersResponse.StatusCode)
                        || string.Equals(customersResponse.StatusCode, "200", StringComparison.OrdinalIgnoreCase));
                var countedOrderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var countedPaymentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var today = DateTime.UtcNow.Date;

                foreach (var customerName in customerNames)
                {
                    var customer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", customerName.Trim())), RestSharp.Method.GET);

                    if (customer == null || customer.StatusCode != "200" || string.IsNullOrWhiteSpace(customer.CustCode))
                    {
                        continue;
                    }

                    var orderResponse = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetLaundryOrders", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "custCode", customer.CustCode }
                    }, Method.GET);

                    var orders = orderResponse?.Orders ?? new List<LaundryOrderDto>();
                    if (orders.Count > 0)
                    {
                        customersWithOrders++;
                    }

                    foreach (var order in orders)
                    {
                        if (!IsDashboardOrderInScope(order, tenantName, storeCode) || order.IsDeleted)
                        {
                            continue;
                        }

                        var orderKey = BuildDashboardOrderAggregationKey(order);
                        var isUniqueOrder = countedOrderKeys.Add(orderKey);

                        if (isUniqueOrder)
                        {
                            var normalizedStatus = NormalizeDashboardOrderStatus(order?.Status);
                            if (string.Equals(normalizedStatus, "ready", StringComparison.Ordinal))
                            {
                                readyForDeliveryCount++;
                            }

                            if (string.Equals(normalizedStatus, "received", StringComparison.Ordinal)
                                || string.Equals(normalizedStatus, "processing", StringComparison.Ordinal)
                                || string.Equals(normalizedStatus, "in process", StringComparison.Ordinal))
                            {
                                pendingProcessingCount++;
                            }
                        }

                        var orderDate = order.CreatedDate.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(order.CreatedDate, DateTimeKind.Utc)
                            : order.CreatedDate.ToUniversalTime();

                        if (orderDate.Date == today && isUniqueOrder)
                        {
                            todayOrders++;

                            if (order.AdvanceUsed > 0)
                            {
                                advanceUsedToday += order.AdvanceUsed;
                            }
                        }

                        if (orderDate.Date == today)
                        {
                            if (order.PaidNow > 0)
                            {
                                var paymentAggregationKey = BuildDashboardPaymentAggregationKey(order);
                                var shouldAggregatePayment = string.IsNullOrWhiteSpace(paymentAggregationKey)
                                    ? isUniqueOrder
                                    : countedPaymentKeys.Add(paymentAggregationKey);

                                if (shouldAggregatePayment)
                                {
                                    amountCollectedToday += order.PaidNow;

                                    var normalizedPaymentMode = NormalizeDashboardPaymentMode(order.PaymentMode);
                                    if (string.Equals(normalizedPaymentMode, "cash", StringComparison.Ordinal))
                                    {
                                        cashCollectedToday += order.PaidNow;
                                    }
                                    else if (string.Equals(normalizedPaymentMode, "upi", StringComparison.Ordinal))
                                    {
                                        upiCollectedToday += order.PaidNow;
                                    }
                                    else if (string.Equals(normalizedPaymentMode, "card", StringComparison.Ordinal))
                                    {
                                        cardCollectedToday += order.PaidNow;
                                    }
                                }
                            }
                        }

                        if (isUniqueOrder && order.NetPayable > 0)
                        {
                            pendingPaymentCount++;
                            pendingPaymentAmount += order.NetPayable;
                        }
                    }
                }

                var deliveryStatus = await _clientApi.SendRequestAsync<LaundryDeliveryStatusResponseDto>("/GetLaundryDeliveryStatus", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode }
                }, Method.GET);

                if (deliveryStatus != null && deliveryStatus.StatusCode == "200")
                {
                    pendingDeliveryCount = deliveryStatus.PendingDeliveryCount;
                }

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    todayOrders,
                    pendingPaymentCount,
                    pendingPaymentAmount = Math.Round(pendingPaymentAmount, 2, MidpointRounding.AwayFromZero),
                    pendingProcessingCount,
                    walkinsToday = todayOrders,
                    advanceUsedToday = Math.Round(advanceUsedToday, 2, MidpointRounding.AwayFromZero),
                    amountCollectedToday = paymentSummaryAvailable
                        ? Math.Round(amountCollectedToday, 2, MidpointRounding.AwayFromZero)
                        : (decimal?)null,
                    cashCollectedToday = paymentSummaryAvailable
                        ? Math.Round(cashCollectedToday, 2, MidpointRounding.AwayFromZero)
                        : (decimal?)null,
                    upiCollectedToday = paymentSummaryAvailable
                        ? Math.Round(upiCollectedToday, 2, MidpointRounding.AwayFromZero)
                        : (decimal?)null,
                    cardCollectedToday = paymentSummaryAvailable
                        ? Math.Round(cardCollectedToday, 2, MidpointRounding.AwayFromZero)
                        : (decimal?)null,
                    cashInHand = (decimal?)null,
                    paymentSummaryAvailable,
                    paymentSummaryReason = paymentSummaryAvailable
                        ? string.Empty
                        : "Payment transactions are unavailable for this store right now.",
                    customersWithOrders,
                    readyForDeliveryCount,
                    pendingDeliveryCount,
                    readyPendingReason = string.Empty
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load live counter dashboard metrics.")
                });
            }
        }

        private static string NormalizeDashboardOrderStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "received";
            }

            var normalizedStatus = status.Trim().ToLowerInvariant();
            return normalizedStatus switch
            {
                "ready for delivery" => "ready",
                "in process" => "processing",
                "cancelled" => "cancelled",
                "canceled" => "cancelled",
                "deleted" => "deleted",
                "completed" => "completed",
                "delivered" => "delivered",
                _ => normalizedStatus
            };
        }

        private static bool IsDashboardOrderInScope(LaundryOrderDto order, string tenantName, string storeCode)
        {
            if (order == null)
            {
                return false;
            }

            var tenantMatches = !string.IsNullOrWhiteSpace(order.TenantName)
                && string.Equals(order.TenantName.Trim(), tenantName?.Trim(), StringComparison.OrdinalIgnoreCase);
            var storeMatches = !string.IsNullOrWhiteSpace(order.StoreCode)
                && string.Equals(order.StoreCode.Trim(), storeCode?.Trim(), StringComparison.OrdinalIgnoreCase);

            return tenantMatches && storeMatches;
        }

        private static string NormalizeDashboardPaymentMode(string paymentMode)
            => string.IsNullOrWhiteSpace(paymentMode)
                ? "cash"
                : paymentMode.Trim().ToLowerInvariant();

        private static string BuildDashboardOrderAggregationKey(LaundryOrderDto order)
        {
            if (order == null)
            {
                return Guid.NewGuid().ToString("N");
            }

            if (!string.IsNullOrWhiteSpace(order.OrderNo))
            {
                return $"ord:{order.OrderNo.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(order.InvoiceNo))
            {
                return $"inv:{order.InvoiceNo.Trim()}";
            }

            return string.Join("|", new[]
            {
                order.CustCode?.Trim() ?? string.Empty,
                order.CreatedDate.ToUniversalTime().ToString("O"),
                order.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                order.NetPayable.ToString("0.00", CultureInfo.InvariantCulture),
                order.PaidNow.ToString("0.00", CultureInfo.InvariantCulture)
            });
        }

        private static string BuildDashboardPaymentAggregationKey(LaundryOrderDto order)
        {
            if (order == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(order.PaymentTransactionId))
            {
                return $"paytxn:{order.PaymentTransactionId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(order.TransactionId))
            {
                return $"txn:{order.TransactionId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(order.PaymentId))
            {
                return $"pay:{order.PaymentId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(order.SettlementId))
            {
                return $"set:{order.SettlementId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(order.ReceiptNo))
            {
                return $"rcpt:{order.ReceiptNo.Trim()}";
            }

            return string.Empty;
        }

        [HttpGet]
        public async Task<JsonResult> GetStoreUsers(string storeCode = null)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can view store users.", users = new List<object>() });
            }

            var tenantEmail = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");
            var tenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrWhiteSpace(tenantEmail)
                || string.IsNullOrWhiteSpace(currentStoreCode)
                || string.IsNullOrWhiteSpace(tenantId))
            {
                return Json(new { success = false, message = "Session expired. Please log in again.", users = new List<object>() });
            }

            if (!TryResolveTargetStore(currentStoreCode, storeCode, out var resolvedStoreCode, out var validationMessage))
            {
                return Json(new { success = false, message = validationMessage, users = new List<object>() });
            }

            try
            {
                var (isValidStore, storeValidationMessage) = await ValidateTenantActiveStoreSelectionAsync(tenantEmail, currentStoreCode, resolvedStoreCode);
                if (!isValidStore)
                {
                    return Json(new { success = false, message = storeValidationMessage, users = new List<object>() });
                }

                var activeTenantStores = await GetActiveTenantStoreCodesAsync(tenantEmail, currentStoreCode);
                if (activeTenantStores.Count == 0)
                {
                    return Json(new { success = true, message = string.Empty, users = new List<object>() });
                }

                var allUsers = new List<object>();
                foreach (var tenantStore in activeTenantStores)
                {
                    JObject response;
                    try
                    {
                        response = await _loginApi.SendRequestAsync<JObject>("/GetStoreUsers", new Dictionary<string, string>
                        {
                            { "tenantId", tenantId },
                            { "tenantEmail", tenantEmail },
                            { "storeCode", tenantStore }
                        }, Method.GET);
                    }
                    catch
                    {
                        continue;
                    }

                    allUsers.AddRange(ExtractStoreUsersForUi(response));
                }

                var users = allUsers
                    .Select(x => JObject.FromObject(x))
                    .Where(x => !string.IsNullOrWhiteSpace(x["email"]?.ToString() ?? x["Email"]?.ToString())
                        && activeTenantStores.Contains(
                            (x["storeCode"]?.ToString() ?? x["StoreCode"]?.ToString() ?? string.Empty).Trim(),
                            StringComparer.OrdinalIgnoreCase))
                    .Select(x => (object)new
                    {
                        email = x["email"]?.ToString() ?? x["Email"]?.ToString() ?? string.Empty,
                        role = x["role"]?.ToString() ?? x["Role"]?.ToString() ?? "StoreUser",
                        isActive = bool.TryParse(x["isActive"]?.ToString() ?? x["IsActive"]?.ToString(), out var isActive) && isActive,
                        createdDate = x["createdDate"]?.ToString() ?? x["CreatedDate"]?.ToString() ?? string.Empty,
                        storeCode = (x["storeCode"]?.ToString() ?? x["StoreCode"]?.ToString() ?? string.Empty).Trim()
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    users
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load store users."),
                    users = new List<object>()
                });
            }
        }

        private static List<object> ExtractStoreUsersForUi(JObject response)
        {
            if (response == null)
            {
                return new List<object>();
            }

            var usersToken = response["Users"]
                             ?? response["users"]
                             ?? response["ResultSet"]?["Users"]
                             ?? response["ResultSet"]?["users"]
                             ?? response["resultSet"]?["Users"]
                             ?? response["resultSet"]?["users"];

            if (usersToken == null)
            {
                return new List<object>();
            }

            var usersArray = usersToken as JArray ?? usersToken["$values"] as JArray;
            if (usersArray == null)
            {
                return new List<object>();
            }

            var results = new List<object>();

            foreach (var token in usersArray)
            {
                if (token is not JObject userObj)
                {
                    continue;
                }

                var createdDateText = string.Empty;
                var createdToken = userObj["CreatedDate"] ?? userObj["createdDate"];
                if (createdToken != null && DateTime.TryParse(createdToken.ToString(), out var createdDate))
                {
                    createdDateText = createdDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }

                results.Add(new
                {
                    email = userObj["Email"]?.ToString() ?? userObj["email"]?.ToString() ?? string.Empty,
                    role = userObj["Role"]?.ToString() ?? userObj["role"]?.ToString() ?? "StoreUser",
                    isActive = bool.TryParse(userObj["IsActive"]?.ToString() ?? userObj["isActive"]?.ToString(), out var isActive) && isActive,
                    createdDate = createdDateText,
                    storeCode = userObj["StoreCode"]?.ToString() ?? userObj["storeCode"]?.ToString() ?? string.Empty
                });
            }

            return results;
        }

        [HttpGet]
        public async Task<IActionResult> StoreUsersSetup()
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access Store Users Setup."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(tenantName, currentStoreCode);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var (tenantStores, loadErrorMessage) = await GetTenantStoreStatusesForNewStoreCreationAsync(tenantName, currentStoreCode);
            var activeStores = tenantStores
                .Where(x => x != null
                    && x.IsActive
                    && !string.IsNullOrWhiteSpace(x.StoreCode)
                    && (string.Equals(x.TenantName, tenantName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.TenantEmail, tenantName, StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(x.TenantName) && string.IsNullOrWhiteSpace(x.TenantEmail)))
                .Select(x => x.StoreCode.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = currentStoreCode;
            ViewData["AllowedStores"] = activeStores;
            ViewData["ActiveStores"] = activeStores;
            ViewData["NoActiveStores"] = activeStores.Count == 0;
            ViewData["StoreUsersSetupStoresLoadError"] = loadErrorMessage ?? string.Empty;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CustomerPreferences(CustomerPreferencesDto model)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            if (model.PickupReminderHours < 0 || model.PickupReminderHours > 168)
            {
                TempData["CustomerAdvancesToast"] = "Pickup reminder should be between 0 and 168 hours.";
                return RedirectToAction(nameof(CustomerAdvances));
            }

            if (model.LoyaltyPointsPerOrder < 0 || model.LoyaltyPointsPerOrder > 100)
            {
                TempData["CustomerAdvancesToast"] = "Loyalty points per order should be between 0 and 100.";
                return RedirectToAction(nameof(CustomerAdvances));
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveCustomerPreferences", model, Method.POST);
                TempData["CustomerPreferencesToast"] = string.IsNullOrWhiteSpace(response?.Message)
                    ? "Customer preferences saved successfully."
                    : response.Message;
            }
            catch (Exception ex)
            {
                var apiErrorMessage = ex?.InnerException?.Message ?? ex?.Message;
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = string.IsNullOrWhiteSpace(apiErrorMessage)
                        ? "Unable to save customer preferences."
                        : $"Unable to save customer preferences. {apiErrorMessage}"
                });

                return View(model);
            }

            return RedirectToAction(nameof(CustomerPreferences));
        }

        private static CustomerPreferencesDto BuildDefaultCustomerPreferences(string tenantName, string storeCode)
        {
            return new CustomerPreferencesDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableSmsNotifications = true,
                EnableEmailNotifications = false,
                AutoGenerateCustomerCode = true,
                RequirePhoneNumber = false,
                RequireEmail = false,
                AllowDuplicatePhoneNumber = false,
                DefaultServiceType = "Dry Clean",
                DefaultPaymentMode = "Cash",
                DefaultStarchLevel = "Medium",
                PickupReminderHours = 24,
                LoyaltyPointsPerOrder = 5
            };
        }

        private static PricingRulesViewModel BuildDefaultPricingRules(string tenantName, string storeCode)
        {
            return new PricingRulesViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableExpressSurcharge = true,
                ExpressSurchargePercent = 15,
                EnableMinimumOrder = false,
                MinimumOrderAmount = 0,
                StandardTurnaroundHours = 48,
                ExpressTurnaroundHours = 24,
                RoundOffInvoiceTotal = true
            };
        }

        private static bool ValidateStoreConfiguration(StoreConfigurationViewModel model, out string validationError)
        {
            var validationContext = new ValidationContext(model);
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(model, validationContext, validationResults, true))
            {
                validationError = validationResults.FirstOrDefault()?.ErrorMessage ?? "Please provide valid store configuration values.";
                return false;
            }

            if (!TimeSpan.TryParse(model.OpeningTime, out var openingTime))
            {
                validationError = "Please provide a valid opening time.";
                return false;
            }

            if (!TimeSpan.TryParse(model.ClosingTime, out var closingTime))
            {
                validationError = "Please provide a valid closing time.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.Email) && !new EmailAddressAttribute().IsValid(model.Email))
            {
                validationError = "Please provide a valid email address.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(model.TimeZoneIana))
            {
                try
                {
                    _ = TimeZoneInfo.FindSystemTimeZoneById(model.TimeZoneIana.Trim());
                }
                catch
                {
                    validationError = "Please provide a valid IANA timezone identifier.";
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(model.PhoneDialingCode)
                && !Regex.IsMatch(model.PhoneDialingCode.Trim(), "^\\+[1-9]\\d{0,6}$"))
            {
                validationError = "Please provide a valid phone country calling code (for example, +91).";
                return false;
            }

            if (openingTime == closingTime)
            {
                validationError = "Opening and closing time cannot be the same.";
                return false;
            }

            validationError = null;
            return true;
        }

        private static string NormalizePhoneNumberForStorage(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            var normalized = phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);
            if (E164Regex.IsMatch(normalized) || LegacyDigitsRegex.IsMatch(normalized))
            {
                return normalized;
            }

            return phone.Trim();
        }

        private async Task<PricingRulesViewModel> LoadPricingRulesAsync(string tenantName, string storeCode)
        {
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<PricingRulesViewModel>("/GetPricingRules", parameters, Method.GET);
                var model = response ?? BuildDefaultPricingRules(tenantName, storeCode);
                model.TenantName = tenantName;
                model.StoreCode = storeCode;
                return model;
            }
            catch (Exception ex) when (IsNotFoundApiResponse(ex))
            {
                return BuildDefaultPricingRules(tenantName, storeCode);
            }
            catch
            {
                return BuildDefaultPricingRules(tenantName, storeCode);
            }
        }

        private static bool ValidatePricingRules(PricingRulesViewModel model, out string validationError)
        {
            var validationContext = new ValidationContext(model);
            var validationResults = new List<ValidationResult>();

            if (!Validator.TryValidateObject(model, validationContext, validationResults, true))
            {
                validationError = validationResults.FirstOrDefault()?.ErrorMessage ?? "Please provide valid pricing rules values.";
                return false;
            }

            if (model.EnableExpressSurcharge && model.ExpressSurchargePercent <= 0)
            {
                validationError = "Express surcharge should be greater than 0 when express surcharge is enabled.";
                return false;
            }

            if (model.EnableMinimumOrder && model.MinimumOrderAmount <= 0)
            {
                validationError = "Minimum order amount should be greater than 0 when minimum order is enabled.";
                return false;
            }

            if (model.ExpressTurnaroundHours > model.StandardTurnaroundHours)
            {
                validationError = "Express turnaround hours cannot be greater than standard turnaround hours.";
                return false;
            }

            validationError = null;
            return true;
        }

        public async Task<JsonResult> GetCustomers()
        {
            if (!TryGetCustomerScope(out _, out _))
            {
                return Json(new List<string>());
            }

            var paramsGetAllStoresByClient = BuildScopedParameters(("eMail", string.Empty));

            var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomers", paramsGetAllStoresByClient, RestSharp.Method.GET);
            return Json(responseMessage?.CustomerNames ?? new List<string>());
        }

        [HttpGet]
        public async Task<JsonResult> SearchCustomers(string searchText)
        {
            if (!TryGetCustomerScope(out _, out _))
            {
                return Json(new List<string>());
            }

            var parameters = BuildScopedParameters(("searchText", searchText));

            try
            {
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SearchCustomers", parameters, Method.GET);
                return Json(responseMessage?.CustomerNames ?? new List<string>());
            }
            catch
            {
                // If the downstream API is unavailable or returns an error, return an empty list
                // so the UI shows a friendly message without throwing a server error.
                return Json(new List<string>());
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCustomerByName(string customerName)
        {
            if (!TryGetCustomerScope(out _, out _))
            {
                return Json(new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Session expired. Please log in again."
                });
            }

            var parameters = BuildScopedParameters(("customerName", customerName));

            CustomerIOResponse responseMessage;

            try
            {
                responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", parameters, Method.GET);
            }
            catch
            {
                // The API answers 404 when the customer does not belong to the logged-in store.
                return Json(new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = CustomerNotInStoreMessage
                });
            }

            if (responseMessage != null
                && string.Equals(responseMessage.StatusCode, "200", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(responseMessage.CustomerName))
            {
                HttpContext.Session.SetString(SelectedCustomerNameSessionKey, responseMessage.CustomerName);
                HttpContext.Session.SetString(SelectedCustomerCodeSessionKey, responseMessage.CustCode ?? string.Empty);

                return Json(responseMessage);
            }

            return Json(new CustomerIOResponse
            {
                StatusCode = "404",
                Message = CustomerNotInStoreMessage
            });
        }

        [HttpPost]
        public async Task<ActionResult> Index(CustomerInfoDto customerInfoDto)
        {
            if (string.IsNullOrWhiteSpace(customerInfoDto.CustomerName))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "Warning.", DisplayMessage = "Please enter Customer Name" });
                return RedirectToAction("Index", "Customer");
            }

            if (string.IsNullOrWhiteSpace(customerInfoDto.Address))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "Warning.", DisplayMessage = "Please enter Customer Address" });
                return RedirectToAction("Index", "Customer");
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var tenanStore = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenanStore))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert alert-danger", Title = "Failed.", DisplayMessage = "Session expired. Please log in again." });
                return RedirectToAction("Login", "Login");
            }

            var phoneValidation = _customerPhoneService.ValidateAndNormalize(
                customerInfoDto.PhoneNumber,
                customerInfoDto.PhoneCountryCode,
                allowLegacyCompatibility: false);
            if (!phoneValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning.",
                    DisplayMessage = phoneValidation.Message
                });
                return RedirectToAction("Index", "Customer");
            }

            if (!string.IsNullOrWhiteSpace(customerInfoDto.Email) && !Regex.IsMatch(customerInfoDto.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "Warning.", DisplayMessage = "Please enter a valid email address" });
                return RedirectToAction("Index", "Customer");
            }

            customerInfoDto.CustomerName = customerInfoDto.CustomerName?.Trim();
            customerInfoDto.Address = customerInfoDto.Address?.Trim();
            // MembershipId removed system-wide
            customerInfoDto.BarCode = string.IsNullOrWhiteSpace(customerInfoDto.BarCode) ? null : customerInfoDto.BarCode.Trim();
            customerInfoDto.PhoneNumber = phoneValidation.NormalizedNumber;
            customerInfoDto.Email = string.IsNullOrWhiteSpace(customerInfoDto.Email) ? null : customerInfoDto.Email.Trim();

            customerInfoDto.StoreCode = tenanStore;
            customerInfoDto.TenantName = tenantName;

            var createRequest = new
            {
                CustomerName = customerInfoDto.CustomerName,
                Address = customerInfoDto.Address,
                BarCode = customerInfoDto.BarCode,
                PhoneNumber = customerInfoDto.PhoneNumber,
                Email = customerInfoDto.Email,
                StoreCode = customerInfoDto.StoreCode,
                TenantName = customerInfoDto.TenantName
            };

            var responseMessage = new CustomerIOResponse();
            try
            {
                responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/InsertCustomer", createRequest, RestSharp.Method.POST);

                var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                    if (responseMessage != null && responseMessage.StatusCode == "200")
                    {
                    if (isAjax)
                    {
                        // Persist customer code and a user message to TempData so the subsequent full-page redirect
                        // can display the generated code and a success message.
                        try
                        {
                            TempData["customerCode"] = responseMessage.CustCode ?? string.Empty;
                            TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                            {
                                CssClassName = "alert-success",
                                Title = "Success!",
                                DisplayMessage = string.IsNullOrWhiteSpace(responseMessage.Message)
                                    ? "Customer added successfully."
                                    : responseMessage.Message
                            });
                        }
                        catch
                        {
                            // ignore TempData failures
                        }

                        return Json(new { success = true, message = string.IsNullOrWhiteSpace(responseMessage.Message) ? "Customer added successfully." : responseMessage.Message, customerCode = responseMessage.CustCode, redirect = Url.Action("Index", "Dashboard") });
                    }

                    TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                    {
                        CssClassName = "alert-success",
                        Title = "Success!",
                        DisplayMessage = string.IsNullOrWhiteSpace(responseMessage.Message)
                            ? "Customer Added successfully."
                            : responseMessage.Message
                    });

                    return RedirectToAction("Index", "Dashboard");
                }

                var failureMessage = string.IsNullOrWhiteSpace(responseMessage?.Message)
                    ? "Unable to save customer."
                    : responseMessage.Message;

                if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = failureMessage });
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert alert-danger", Title = "Failed.", DisplayMessage = failureMessage });
                return RedirectToAction("Index", "Customer");
            }
            catch (Exception ex)
            {
                var apiErrorMessage = ex?.InnerException?.Message ?? ex?.Message;
                var displayMessage = string.IsNullOrWhiteSpace(apiErrorMessage)
                    ? "Unable to save customer."
                    : $"Unable to save customer. {apiErrorMessage}";

                if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = displayMessage });
                }

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = displayMessage
                });
                return RedirectToAction("Index", "Customer");
            }
        }

        [HttpGet]
        public JsonResult ValidatePhone(string phone, string countryIso2)
        {
            var validation = _customerPhoneService.ValidateAndNormalize(phone, countryIso2, allowLegacyCompatibility: false);
            return Json(new
            {
                isValid = validation.IsValid,
                normalizedNumber = validation.NormalizedNumber,
                localNumber = validation.IsValid
                    ? _customerPhoneService.GetLocalNumber(phone, countryIso2)
                    : null,
                formattedNationalNumber = validation.FormattedNationalNumber,
                message = validation.Message
            });
        }

        [HttpPost]
        public async Task<JsonResult> UpdateCustomer(CustomerInfoDto customerInfoDto)
        {
            customerInfoDto.customerCode = customerInfoDto.customerCode?.Trim();

            if (string.IsNullOrWhiteSpace(customerInfoDto.customerCode) && !string.IsNullOrWhiteSpace(customerInfoDto.CustomerName))
            {
                var findParams = BuildScopedParameters(("customerName", customerInfoDto.CustomerName.Trim()));

                var existingCustomer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", findParams, Method.GET);
                if (existingCustomer != null && existingCustomer.StatusCode == "200")
                {
                    customerInfoDto.customerCode = existingCustomer.CustCode?.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(customerInfoDto.customerCode))
            {
                return Json(new { success = false, message = "Please search and select a customer first" });
            }

            if (string.IsNullOrWhiteSpace(customerInfoDto.CustomerName))
            {
                return Json(new { success = false, message = "Please enter Customer Name" });
            }

            if (string.IsNullOrWhiteSpace(customerInfoDto.Address))
            {
                return Json(new { success = false, message = "Please enter Customer Address" });
            }

            customerInfoDto.StoreCode = HttpContext.Session.GetString("TenantStore");
            customerInfoDto.TenantName = HttpContext.Session.GetString("TenantName");

            if (string.IsNullOrWhiteSpace(customerInfoDto.StoreCode) || string.IsNullOrWhiteSpace(customerInfoDto.TenantName))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            var phoneValidation = _customerPhoneService.ValidateAndNormalize(
                customerInfoDto.PhoneNumber,
                customerInfoDto.PhoneCountryCode,
                allowLegacyCompatibility: false);
            var normalizedUpdatePhone = phoneValidation.NormalizedNumber;
            if (!phoneValidation.IsValid)
            {
                return Json(new { success = false, message = phoneValidation.Message });
            }

            if (!string.IsNullOrWhiteSpace(customerInfoDto.Email) && !Regex.IsMatch(customerInfoDto.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                return Json(new { success = false, message = "Please enter a valid email address" });
            }

            customerInfoDto.CustomerName = customerInfoDto.CustomerName?.Trim();
            customerInfoDto.Address = customerInfoDto.Address?.Trim();
            customerInfoDto.BarCode = string.IsNullOrWhiteSpace(customerInfoDto.BarCode) ? null : customerInfoDto.BarCode.Trim();
            customerInfoDto.PhoneNumber = normalizedUpdatePhone;
            customerInfoDto.Email = string.IsNullOrWhiteSpace(customerInfoDto.Email) ? null : customerInfoDto.Email.Trim();

            var updateRequest = new
            {
                CustomerName = customerInfoDto.CustomerName,
                Address = customerInfoDto.Address,
                BarCode = customerInfoDto.BarCode,
                PhoneNumber = customerInfoDto.PhoneNumber,
                Email = customerInfoDto.Email,
                StoreCode = customerInfoDto.StoreCode,
                TenantName = customerInfoDto.TenantName,
                CustCode = customerInfoDto.customerCode
            };

            try
            {
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/UpdateCustomer", updateRequest, Method.PUT);
                return Json(new
                {
                    success = responseMessage != null && responseMessage.StatusCode == "200",
                    message = responseMessage?.Message ?? "Unable to update customer.",
                    customerCode = responseMessage?.CustCode
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update customer.", "Phone number already exists for this store.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> DeleteCustomer(string customerCode, string customerName = null)
        {
            customerCode = customerCode?.Trim();

            if (string.IsNullOrWhiteSpace(customerCode) && !string.IsNullOrWhiteSpace(customerName))
            {
                var findParams = BuildScopedParameters(("customerName", customerName.Trim()));

                var existingCustomer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", findParams, Method.GET);
                if (existingCustomer != null && existingCustomer.StatusCode == "200")
                {
                    customerCode = existingCustomer.CustCode?.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(customerCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Please search and select a customer first"
                });
            }

            var parameters = new Dictionary<string, string>
            {
                { "custCode", customerCode }
            };

            try
            {
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/DeleteCustomer", parameters, Method.DELETE);
                return Json(new
                {
                    success = responseMessage != null && responseMessage.StatusCode == "200",
                    message = responseMessage?.Message ?? "Unable to delete customer."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to delete customer.")
                });
            }
        }

        [HttpGet]
        public IActionResult CustomerAdvances(string customerName = null, string customerCode = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var model = new CustomerAdvanceDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                CustomerName = customerName,
                CustCode = customerCode,
                TransactionType = "Credit"
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CustomerAdvances(CustomerAdvanceDto model)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CustomerName = model.CustomerName?.Trim();
            model.CustCode = model.CustCode?.Trim();
            model.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            model.TransactionType = string.IsNullOrWhiteSpace(model.TransactionType) ? "Credit" : model.TransactionType.Trim();

            if (string.IsNullOrWhiteSpace(model.CustomerName))
            {
                TempData["CustomerAdvancesToast"] = "Please enter customer name.";
                return RedirectToAction(nameof(CustomerAdvances));
            }

            if (model.AdvanceAmount <= 0)
            {
                TempData["CustomerAdvancesToast"] = "Advance amount should be greater than zero.";
                return RedirectToAction(nameof(CustomerAdvances), new { customerName = model.CustomerName, customerCode = model.CustCode });
            }

            if (!string.Equals(model.TransactionType, "Credit", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(model.TransactionType, "Debit", StringComparison.OrdinalIgnoreCase))
            {
                TempData["CustomerAdvancesToast"] = "Transaction type should be Credit or Debit.";
                return RedirectToAction(nameof(CustomerAdvances), new { customerName = model.CustomerName, customerCode = model.CustCode });
            }

            CustomerIOResponse scopedCustomer;
            try
            {
                scopedCustomer = await ResolveCustomerByNameAsync(model.CustomerName);
            }
            catch
            {
                TempData["CustomerAdvancesToast"] = "Customer not found in the current store.";
                return RedirectToAction(nameof(CustomerAdvances), new { customerName = model.CustomerName });
            }

            if (scopedCustomer == null || scopedCustomer.StatusCode != "200" || string.IsNullOrWhiteSpace(scopedCustomer.CustCode))
            {
                TempData["CustomerAdvancesToast"] = "Customer not found in the current store.";
                return RedirectToAction(nameof(CustomerAdvances), new { customerName = model.CustomerName });
            }

            model.CustCode = scopedCustomer.CustCode?.Trim();
            model.CustomerName = string.IsNullOrWhiteSpace(scopedCustomer.CustomerName)
                ? model.CustomerName
                : scopedCustomer.CustomerName;

            try
            {
                var saveRequest = new
                {
                    TenantName = model.TenantName,
                    StoreCode = model.StoreCode,
                    CustCode = model.CustCode,
                    AdvanceAmount = model.AdvanceAmount,
                    TransactionType = model.TransactionType,
                    Notes = model.Notes
                };

                var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveCustomerAdvance", saveRequest, Method.POST);
                var isSaveSuccess = saveResponse != null
                    && string.Equals(saveResponse.StatusCode, "200", StringComparison.OrdinalIgnoreCase);

                if (isSaveSuccess)
                {
                    TempData["CustomerAdvancesToast"] = string.IsNullOrWhiteSpace(saveResponse?.Message)
                        ? "Customer advance saved successfully."
                        : saveResponse.Message;
                }
                else
                {
                    TempData["CustomerAdvancesToast"] = string.IsNullOrWhiteSpace(saveResponse?.Message)
                        ? "Unable to save customer advance."
                        : saveResponse.Message;
                }
            }
            catch (Exception ex)
            {
                TempData["CustomerAdvancesToast"] = BuildApiErrorMessage(ex, "Unable to save customer advance.");
            }

            return RedirectToAction(nameof(CustomerAdvances), new
            {
                customerName = model.CustomerName,
                customerCode = model.CustCode
            });
        }

        [HttpGet]
        public async Task<JsonResult> GetCustomerAdvances(string customerName, string customerCode = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            var resolvedCustomerCode = customerCode?.Trim();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again.",
                    balance = 0m,
                    transactions = new List<CustomerAdvanceDto>()
                });
            }

            customerName = customerName?.Trim();
            customerCode = customerCode?.Trim();
            resolvedCustomerCode = customerCode;

            if (string.IsNullOrWhiteSpace(customerName) && string.IsNullOrWhiteSpace(customerCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Please enter customer name.",
                    balance = 0m,
                    transactions = new List<CustomerAdvanceDto>()
                });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(resolvedCustomerCode) && !string.IsNullOrWhiteSpace(customerName))
                {
                    var customer = await ResolveCustomerByNameAsync(customerName);
                    if (customer == null || customer.StatusCode != "200" || string.IsNullOrWhiteSpace(customer.CustCode))
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Customer not found.",
                            balance = 0m,
                            transactions = new List<CustomerAdvanceDto>()
                        });
                    }

                    resolvedCustomerCode = customer.CustCode?.Trim();
                }

                if (string.IsNullOrWhiteSpace(resolvedCustomerCode))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Customer not found in the current store.",
                        balance = 0m,
                        transactions = new List<CustomerAdvanceDto>()
                    });
                }

                var response = await _clientApi.SendRequestAsync<CustomerAdvanceResponseDto>("/GetCustomerAdvances", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "custCode", resolvedCustomerCode }
                }, Method.GET);

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    customerCode = resolvedCustomerCode,
                    balance = response?.Balance ?? 0m,
                    transactions = response?.Transactions ?? new List<CustomerAdvanceDto>()
                });
            }
            catch (Exception ex)
            {
                var rawMessage = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
                if (rawMessage.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                    || rawMessage.Contains("404", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        customerCode = resolvedCustomerCode,
                        balance = 0m,
                        transactions = new List<CustomerAdvanceDto>()
                    });
                }

                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to fetch customer advances."),
                    balance = 0m,
                    transactions = new List<CustomerAdvanceDto>()
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> SetupCreateOrder(string customerName = null, string customerCode = null, string orderMode = null, bool setupOnly = false, string orderSearch = null, bool showPostActions = false)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var currencyResolution = await _currencyService.ResolveAsync(tenantName, storeCode);

            await EnsureDefaultWalkInCustomerInitializedAsync(tenantName, storeCode);

            var selectedCustomerName = string.IsNullOrWhiteSpace(customerName)
                ? HttpContext.Session.GetString(SelectedCustomerNameSessionKey)
                : customerName.Trim();

            var selectedCustomerCode = string.IsNullOrWhiteSpace(customerCode)
                ? HttpContext.Session.GetString(SelectedCustomerCodeSessionKey)
                : customerCode.Trim();

            if (!setupOnly && !string.IsNullOrWhiteSpace(selectedCustomerName) && string.IsNullOrWhiteSpace(selectedCustomerCode))
            {
                try
                {
                    var existingCustomer = await ResolveCustomerByNameAsync(selectedCustomerName);
                    if (existingCustomer != null
                        && string.Equals(existingCustomer.StatusCode, "200", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedCustomerName = string.IsNullOrWhiteSpace(existingCustomer.CustomerName)
                            ? selectedCustomerName
                            : existingCustomer.CustomerName;
                        selectedCustomerCode = existingCustomer.CustCode?.Trim();
                    }
                }
                catch
                {
                    // Best effort only. Setup page can still load customer details client-side by name.
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedCustomerName))
            {
                HttpContext.Session.SetString(SelectedCustomerNameSessionKey, selectedCustomerName);
            }

            if (!string.IsNullOrWhiteSpace(selectedCustomerCode))
            {
                HttpContext.Session.SetString(SelectedCustomerCodeSessionKey, selectedCustomerCode);
            }

            var paymentSettings = await LoadPaymentSettingsAsync(tenantName, storeCode);
            var enabledPaymentModes = GetEnabledPaymentModes(paymentSettings);

            if (enabledPaymentModes.Count == 0)
            {
                enabledPaymentModes = new List<string> { "Cash" };
            }

            var defaultPaymentMode = enabledPaymentModes.FirstOrDefault(x =>
                string.Equals(x, paymentSettings.DefaultPaymentMode, StringComparison.OrdinalIgnoreCase))
                ?? enabledPaymentModes[0];

            var model = new LaundryOrderDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                CustomerName = selectedCustomerName,
                CustCode = selectedCustomerCode,
                OrderMode = "pieces",
                WeightInKg = 0,
                RatePerKg = 0,
                ServiceType = "Dry Clean",
                PaymentMode = defaultPaymentMode
            };

            ViewData["EnabledPaymentModes"] = enabledPaymentModes;
            ViewData["AllowPartialPayment"] = paymentSettings.AllowPartialPayment;
            ViewData["AllowCredit"] = paymentSettings.AllowCredit;
            ViewData["RoundOffPayableAmount"] = paymentSettings.RoundOffPayableAmount;
            ViewData["CurrencyResolution"] = currencyResolution;
            ViewData["CurrencyWarning"] = currencyResolution.IsResolved ? null : currencyResolution.Warning;

            var pricingRules = await LoadPricingRulesAsync(tenantName, storeCode);
            ViewData["EnableMinimumOrder"] = pricingRules.EnableMinimumOrder;
            ViewData["MinimumOrderAmount"] = pricingRules.MinimumOrderAmount;

            var taxContext = await LoadCreateOrderTaxContextAsync(tenantName, storeCode);
            ViewData["TaxEnabled"] = taxContext.EnableTax;
            ViewData["GstPercent"] = taxContext.TotalRatePercent;
            ViewData["PricesIncludeTax"] = taxContext.PricesIncludeTax;
            ViewData["TaxMode"] = taxContext.TaxMode;
            ViewData["TaxComponentsJson"] = JsonConvert.SerializeObject(
                taxContext.Components.Select(x => new
                {
                    code = x.Code,
                    displayName = x.DisplayName,
                    ratePercent = x.RatePercent,
                    jurisdiction = x.Jurisdiction,
                    priority = x.Priority,
                    isCompound = x.IsCompound
                }).ToList());

            if (!string.IsNullOrWhiteSpace(orderMode))
            {
                var normalizedMode = orderMode.Trim().ToLowerInvariant();
                if (normalizedMode == "weight" || normalizedMode == "pieces")
                {
                    model.OrderMode = normalizedMode;
                }

                ViewData["OrderMode"] = model.OrderMode;
            }

            ViewData["SetupOnly"] = setupOnly;

            // Expose admin flag to the view so price-config controls can be gated client-side
            ViewData["IsAdminUser"] = IsAdminUser();

            var completionPayload = Convert.ToString(TempData["OrderCompletionContext"]);
            if (showPostActions && !string.IsNullOrWhiteSpace(completionPayload))
            {
                ViewData["OrderCompletionContext"] = completionPayload;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetupCreateOrder(LaundryOrderDto model)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            var scopeValidation = await ValidateCurrentStoreAccessAsync(
                tenantName,
                storeCode,
                requireOrderPermission: true,
                requireSubscriptionEligibility: true);

            if (!scopeValidation.IsValid)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = scopeValidation.Message
                });

                return RedirectToAction("Index", "Dashboard");
            }

            var currencyResolution = await _currencyService.ResolveAsync(tenantName, storeCode);
            if (!currencyResolution.IsResolved)
            {
                TempData["SetupCreateOrderToast"] = currencyResolution.Warning;
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model?.CustomerName, customerCode = model?.CustCode, orderMode = model?.OrderMode });
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CustomerName = model.CustomerName?.Trim();
            model.CustCode = model.CustCode?.Trim();
            model.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            model.ServiceType = string.IsNullOrWhiteSpace(model.ServiceType) ? "Dry Clean" : model.ServiceType.Trim();
            model.OrderMode = string.IsNullOrWhiteSpace(model.OrderMode) ? "pieces" : model.OrderMode.Trim().ToLowerInvariant();
            model.PaymentMode = string.IsNullOrWhiteSpace(model.PaymentMode) ? "Cash" : model.PaymentMode.Trim();

            if (model.OrderMode != "pieces" && model.OrderMode != "weight")
            {
                model.OrderMode = "pieces";
            }

            if (model.OrderMode == "weight")
            {
                if (model.WeightInKg <= 0)
                {
                    TempData["SetupCreateOrderToast"] = "Weight should be greater than zero for weight-based orders.";
                    return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
                }

                if (model.RatePerKg <= 0)
                {
                    TempData["SetupCreateOrderToast"] = "Rate per kg should be greater than zero for weight-based orders.";
                    return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
                }

                model.OrderAmount = Math.Round(model.WeightInKg * model.RatePerKg, 2, MidpointRounding.AwayFromZero);
            }

            if (string.IsNullOrWhiteSpace(model.CustomerName))
            {
                TempData["SetupCreateOrderToast"] = "Please enter customer name.";
                return RedirectToAction(nameof(SetupCreateOrder));
            }

            if (model.OrderAmount <= 0)
            {
                TempData["SetupCreateOrderToast"] = "Order amount should be greater than zero.";
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode });
            }

            if (model.AdvanceUsed < 0)
            {
                TempData["SetupCreateOrderToast"] = "Advance used cannot be negative.";
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode });
            }

            model.OrderAmount = Math.Round(Math.Max(0m, model.OrderAmount), 2, MidpointRounding.AwayFromZero);
            model.AdvanceUsed = Math.Round(model.AdvanceUsed, 2, MidpointRounding.AwayFromZero);

            var paymentSettings = await LoadPaymentSettingsAsync(tenantName, storeCode);
            var allowPartialPayment = paymentSettings.AllowPartialPayment;
            var enabledPaymentModes = GetEnabledPaymentModes(paymentSettings);

            if (enabledPaymentModes.Count == 0)
            {
                if (model.PaidNow > 0)
                {
                    TempData["SetupCreateOrderToast"] = "No payment mode is configured for this store. Save Payment Settings before collecting payment.";
                    return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
                }

                model.PaymentMode = "Cash";
            }
            else
            {
                var resolvedPaymentMode = enabledPaymentModes.FirstOrDefault(x => string.Equals(x, model.PaymentMode, StringComparison.OrdinalIgnoreCase))
                    ?? enabledPaymentModes.FirstOrDefault(x => string.Equals(x, paymentSettings.DefaultPaymentMode, StringComparison.OrdinalIgnoreCase))
                    ?? enabledPaymentModes[0];

                model.PaymentMode = resolvedPaymentMode;
            }

            var taxContext = await LoadCreateOrderTaxContextAsync(tenantName, storeCode);
            if (taxContext.IsConfigurationMissing)
            {
                TempData["SetupCreateOrderToast"] = "Tax configuration is missing for this store. Please review Tax & Invoice Settings before creating orders.";
                return RedirectToAction(nameof(TaxInvoiceSettings));
            }

            var taxEnabled = taxContext.EnableTax;
            var payableBeforeAdvance = model.OrderAmount;
            var taxComputation = ComputeCreateOrderTaxSummary(model.OrderAmount, taxContext);
            payableBeforeAdvance = taxComputation.TotalAmount;

            model.SubTotal = taxComputation.SubTotal;
            model.TaxAmount = taxComputation.TaxAmount;
            model.TotalAmount = taxComputation.TotalAmount;
            model.TaxPercent = taxComputation.TaxPercent;
            model.CgstAmount = taxComputation.CgstAmount;
            model.SgstAmount = taxComputation.SgstAmount;

            if (model.AdvanceUsed > payableBeforeAdvance)
            {
                model.AdvanceUsed = payableBeforeAdvance;
            }

            var netPayableAfterAdvance = Math.Round(Math.Max(0m, payableBeforeAdvance - model.AdvanceUsed), 2, MidpointRounding.AwayFromZero);

            if (model.PaidNow < 0)
            {
                TempData["SetupCreateOrderToast"] = "Paid amount cannot be negative.";
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
            }

            if (model.PaidNow > netPayableAfterAdvance)
            {
                TempData["SetupCreateOrderToast"] = "Paid amount cannot be greater than net payable.";
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
            }

            model.PaidNow = Math.Round(model.PaidNow, 2, MidpointRounding.AwayFromZero);

            if (!allowPartialPayment)
            {
                const decimal paymentTolerance = 0.01m;

                var remainingAfterPayment = netPayableAfterAdvance - model.PaidNow;
                var isUnpaidOrder = model.PaidNow <= paymentTolerance;
                var isFullyPaidOrder = remainingAfterPayment <= paymentTolerance;

                if (!isUnpaidOrder && !isFullyPaidOrder)
                {
                    TempData["SetupCreateOrderToast"] = "Partial payments are disabled for this store. Please use customer's advance or collect full payment before creating the order.";
                    return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName, customerCode = model.CustCode, orderMode = model.OrderMode });
                }

                if (isFullyPaidOrder)
                {
                    model.PaidNow = netPayableAfterAdvance;
                }
            }

            model.NetPayable = netPayableAfterAdvance;
            model.PendingAmount = Math.Round(Math.Max(0m, model.NetPayable - model.PaidNow), 2, MidpointRounding.AwayFromZero);

            if (string.IsNullOrWhiteSpace(model.CustCode))
            {
                var sessionCustomerName = HttpContext.Session.GetString(SelectedCustomerNameSessionKey)?.Trim();
                var sessionCustomerCode = HttpContext.Session.GetString(SelectedCustomerCodeSessionKey)?.Trim();

                if (!string.IsNullOrWhiteSpace(sessionCustomerCode)
                    && !string.IsNullOrWhiteSpace(sessionCustomerName)
                    && string.Equals(sessionCustomerName, model.CustomerName, StringComparison.OrdinalIgnoreCase))
                {
                    model.CustCode = sessionCustomerCode;
                }
            }

            if (string.IsNullOrWhiteSpace(model.CustCode))
            {
                try
                {
                    var existingCustomer = await ResolveCustomerByNameAsync(model.CustomerName);
                    if (existingCustomer != null && existingCustomer.StatusCode == "200")
                    {
                        model.CustCode = existingCustomer.CustCode?.Trim();
                        model.CustomerName = existingCustomer.CustomerName;
                    }
                }
                catch
                {
                    TempData["SetupCreateOrderToast"] = "Customer not loaded. Please click Load Customer and select a valid customer.";
                    return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName });
                }
            }

            if (string.IsNullOrWhiteSpace(model.CustCode))
            {
                TempData["SetupCreateOrderToast"] = "Customer not loaded. Please click Load Customer and select a valid customer.";
                return RedirectToAction(nameof(SetupCreateOrder), new { customerName = model.CustomerName });
            }

            List<LaundryOrderItemDto> submittedItems = new List<LaundryOrderItemDto>();

            if (!string.IsNullOrWhiteSpace(model.OrderItemsJson))
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject<List<LaundryOrderItemDto>>(model.OrderItemsJson);
                    if (parsed != null)
                    {
                        submittedItems = parsed
                            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ItemName) && x.Quantity > 0)
                            .Select(x => new LaundryOrderItemDto
                            {
                                ServiceType = string.IsNullOrWhiteSpace(x.ServiceType) ? model.ServiceType : x.ServiceType.Trim(),
                                Category = x.Category?.Trim(),
                                ItemName = x.ItemName?.Trim(),
                                UnitPrice = x.UnitPrice,
                                Quantity = x.Quantity > 0 ? x.Quantity : 1,
                                TagNo = x.TagNo?.Trim()
                            })
                            .ToList();
                    }
                }
                catch
                {
                    submittedItems = new List<LaundryOrderItemDto>();
                }
            }

            var orderCreated = false;

            try
            {
                var saveRequest = new
                {
                    TenantName = model.TenantName,
                    StoreCode = model.StoreCode,
                    CustCode = model.CustCode,
                    CustomerName = model.CustomerName,
                    ServiceType = model.ServiceType,
                    OrderMode = model.OrderMode,
                    WeightInKg = model.OrderMode == "weight" ? model.WeightInKg : 0,
                    RatePerKg = model.OrderMode == "weight" ? model.RatePerKg : 0,
                    OrderAmount = model.OrderAmount,
                    AdvanceUsed = model.AdvanceUsed,
                    PaidNow = model.PaidNow,
                    PendingAmount = model.PendingAmount,
                    NetPayable = model.PendingAmount,
                    PaymentMode = model.PaymentMode,
                    Notes = model.Notes,
                    TotalPieces = model.OrderMode == "pieces"
                        ? submittedItems.Sum(x => x?.Quantity > 0 ? x.Quantity : 1)
                        : 0,
                    Items = model.OrderMode == "pieces" ? submittedItems : new List<LaundryOrderItemDto>()
                };

                var saveResponse = await _clientApi.SendRequestAsync<JObject>("/CreateLaundryOrder", saveRequest, Method.POST);

                var orderNo = saveResponse?["OrderNo"]?.ToString()
                              ?? saveResponse?["orderNo"]?.ToString()
                              ?? saveResponse?["ResultSet"]?["OrderNo"]?.ToString()
                              ?? saveResponse?["ResultSet"]?["orderNo"]?.ToString()
                              ?? saveResponse?["resultSet"]?["OrderNo"]?.ToString()
                              ?? saveResponse?["resultSet"]?["orderNo"]?.ToString();

                var invoiceNo = saveResponse?["InvoiceNo"]?.ToString()
                                ?? saveResponse?["invoiceNo"]?.ToString()
                                ?? saveResponse?["ResultSet"]?["InvoiceNo"]?.ToString()
                                ?? saveResponse?["ResultSet"]?["invoiceNo"]?.ToString()
                                ?? saveResponse?["resultSet"]?["InvoiceNo"]?.ToString()
                                ?? saveResponse?["resultSet"]?["invoiceNo"]?.ToString();

                var savedCustCode = saveResponse?["CustCode"]?.ToString()
                                  ?? saveResponse?["custCode"]?.ToString()
                                  ?? saveResponse?["ResultSet"]?["CustCode"]?.ToString()
                                  ?? saveResponse?["ResultSet"]?["custCode"]?.ToString()
                                  ?? saveResponse?["resultSet"]?["CustCode"]?.ToString()
                                  ?? saveResponse?["resultSet"]?["custCode"]?.ToString();

                if (!string.IsNullOrWhiteSpace(savedCustCode))
                {
                    model.CustCode = savedCustCode.Trim();
                }

                var apiMessage = saveResponse?["Message"]?.ToString()
                                 ?? saveResponse?["message"]?.ToString()
                                 ?? saveResponse?["ResultSet"]?["Message"]?.ToString()
                                 ?? saveResponse?["ResultSet"]?["message"]?.ToString()
                                 ?? saveResponse?["resultSet"]?["Message"]?.ToString()
                                 ?? saveResponse?["resultSet"]?["message"]?.ToString();

                if (string.IsNullOrWhiteSpace(orderNo))
                {
                    TempData["SetupCreateOrderToast"] = string.IsNullOrWhiteSpace(apiMessage)
                        ? "Order could not be created. Please verify customer, pricing, and amount, then try again."
                        : apiMessage;
                }
                else
                {
                    HttpContext.Session.SetString(SelectedCustomerNameSessionKey, model.CustomerName ?? string.Empty);
                    HttpContext.Session.SetString(SelectedCustomerCodeSessionKey, model.CustCode ?? string.Empty);

                    var responseItems = new List<LaundryOrderItemDto>();
                    var itemsToken = saveResponse?["Items"]
                                     ?? saveResponse?["items"]
                                     ?? saveResponse?["ResultSet"]?["Items"]
                                     ?? saveResponse?["ResultSet"]?["items"]
                                     ?? saveResponse?["resultSet"]?["Items"]
                                     ?? saveResponse?["resultSet"]?["items"];

                    if (itemsToken != null)
                    {
                        try
                        {
                            responseItems = itemsToken.ToObject<List<LaundryOrderItemDto>>() ?? new List<LaundryOrderItemDto>();
                        }
                        catch
                        {
                            responseItems = new List<LaundryOrderItemDto>();
                        }
                    }

                    if (responseItems.Count == 0)
                    {
                        responseItems = submittedItems;
                    }

                    var createdTotalPieces = submittedItems.Sum(x => x?.Quantity > 0 ? x.Quantity : 1);
                    if (createdTotalPieces > 0 && !string.IsNullOrWhiteSpace(orderNo))
                    {
                        SaveOrderPieces(orderNo, createdTotalPieces);
                    }

                    decimal ReadResponseAmount(string propertyName)
                    {
                        var token = saveResponse?[propertyName]
                                    ?? saveResponse?[char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1)]
                                    ?? saveResponse?["ResultSet"]?[propertyName]
                                    ?? saveResponse?["ResultSet"]?[char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1)]
                                    ?? saveResponse?["resultSet"]?[propertyName]
                                    ?? saveResponse?["resultSet"]?[char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1)];

                        return decimal.TryParse(Convert.ToString(token), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : decimal.MinValue;
                    }

                    var responseOrderAmount = ReadResponseAmount("OrderAmount");
                    var responseSubTotal = ReadResponseAmount("SubTotal");
                    var responseTaxPercent = ReadResponseAmount("TaxPercent");
                    var responseTaxAmount = ReadResponseAmount("TaxAmount");
                    var responseCgstAmount = ReadResponseAmount("CgstAmount");
                    var responseSgstAmount = ReadResponseAmount("SgstAmount");
                    var responseTotalAmount = ReadResponseAmount("TotalAmount");
                    var responseAdvanceUsed = ReadResponseAmount("AdvanceUsed");
                    var responsePaidNow = ReadResponseAmount("PaidNow");
                    var responsePendingAmount = ReadResponseAmount("PendingAmount");
                    var responseNetPayable = ReadResponseAmount("NetPayable");

                    var resolvedOrderAmount = responseOrderAmount != decimal.MinValue ? responseOrderAmount : model.OrderAmount;
                    var resolvedSubTotal = responseSubTotal != decimal.MinValue ? responseSubTotal : resolvedOrderAmount;
                    var resolvedTaxPercent = responseTaxPercent != decimal.MinValue ? responseTaxPercent : model.TaxPercent;
                    var resolvedTaxAmount = responseTaxAmount != decimal.MinValue ? responseTaxAmount : model.TaxAmount;
                    var resolvedCgstAmount = responseCgstAmount != decimal.MinValue ? responseCgstAmount : model.CgstAmount;
                    var resolvedSgstAmount = responseSgstAmount != decimal.MinValue ? responseSgstAmount : model.SgstAmount;
                    var resolvedTotalAmount = responseTotalAmount != decimal.MinValue
                        ? responseTotalAmount
                        : Math.Round(Math.Max(0m, resolvedSubTotal + resolvedTaxAmount), 2, MidpointRounding.AwayFromZero);
                    var resolvedAdvanceUsed = responseAdvanceUsed != decimal.MinValue ? responseAdvanceUsed : model.AdvanceUsed;
                    var resolvedPaidNow = responsePaidNow != decimal.MinValue ? responsePaidNow : model.PaidNow;
                    var resolvedPendingAmount = responsePendingAmount != decimal.MinValue
                        ? responsePendingAmount
                        : (responseNetPayable != decimal.MinValue ? responseNetPayable : model.PendingAmount);

                    var summary = ResolveOrderFinancialSummary(
                        resolvedOrderAmount,
                        resolvedSubTotal,
                        resolvedTaxAmount,
                        resolvedTotalAmount,
                        resolvedAdvanceUsed,
                        resolvedPaidNow,
                        resolvedPendingAmount,
                        resolvedPendingAmount);

                    var customerProfile = await ResolveCustomerByNameAsync(model.CustomerName);
                    var completionContext = new OrderCompletionContextDto
                    {
                        TenantName = model.TenantName,
                        StoreCode = model.StoreCode,
                        OrderNo = orderNo,
                        InvoiceNo = string.IsNullOrWhiteSpace(invoiceNo) ? orderNo : invoiceNo,
                        CustCode = model.CustCode,
                        CustomerName = model.CustomerName,
                        CustomerEmail = customerProfile?.Email,
                        OrderMode = model.OrderMode,
                        ServiceType = model.ServiceType,
                        OrderAmount = summary.OrderAmount,
                        SubTotal = summary.SubTotal,
                        TaxPercent = resolvedTaxPercent,
                        TaxAmount = summary.TaxAmount,
                        CgstAmount = resolvedCgstAmount,
                        SgstAmount = resolvedSgstAmount,
                        TotalAmount = summary.TotalAmount,
                        AdvanceUsed = summary.AdvanceUsed,
                        PaidNow = summary.PaidNow,
                        PendingAmount = summary.PendingAmount,
                        NetPayable = summary.NetPayable,
                        PaymentMode = model.PaymentMode,
                        CreatedDate = DateTime.UtcNow,
                        TotalPieces = createdTotalPieces,
                        Items = responseItems
                    };

                    var snapshotTaxLines = await TryGetOrderTaxLinesAsync(model.TenantName, model.StoreCode, orderNo);
                    completionContext.TaxLines = snapshotTaxLines.Count > 0
                        ? snapshotTaxLines
                        : BuildLegacyTaxFallbackLines(completionContext);

                    var orderToastMessage = $"Order created successfully. Order No: {orderNo}";
                    var autoEmailAddress = completionContext.CustomerEmail?.Trim();
                    if (!string.IsNullOrWhiteSpace(autoEmailAddress) && new EmailAddressAttribute().IsValid(autoEmailAddress))
                    {
                        var autoEmailResult = await SendOrderBillEmailInternalAsync(completionContext, autoEmailAddress);
                        if (autoEmailResult.Success)
                        {
                            orderToastMessage += " Bill emailed successfully.";
                            await LogOrderActionAuditAsync(completionContext, "EmailBillAuto", "Bill emailed automatically after order creation.", autoEmailAddress);
                        }
                        else
                        {
                            orderToastMessage += " Order created successfully, but the email could not be sent. Please check email configuration or retry.";
                            await LogOrderActionAuditAsync(completionContext, "EmailBillAutoFailed", autoEmailResult.Message, autoEmailAddress);
                        }
                    }
                    else
                    {
                        await LogOrderActionAuditAsync(completionContext, "EmailBillAutoSkipped", "Automatic bill email skipped. Customer email missing.");
                    }

                    TempData["SetupCreateOrderToast"] = orderToastMessage;

                    TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(completionContext);
                    await LogOrderActionAuditAsync(completionContext, "OrderCreated", "Laundry order created successfully.");
                    orderCreated = true;
                }
            }
            catch (Exception ex)
            {
                TempData["SetupCreateOrderToast"] = BuildApiErrorMessage(ex, "Unable to create order.");
            }

            if (!orderCreated)
            {
                var postPaymentSettings = await LoadPaymentSettingsAsync(tenantName, storeCode);
                var postEnabledPaymentModes = GetEnabledPaymentModes(postPaymentSettings);
                if (postEnabledPaymentModes.Count == 0)
                {
                    postEnabledPaymentModes = new List<string> { "Cash" };
                }

                ViewData["EnabledPaymentModes"] = postEnabledPaymentModes;
                ViewData["AllowPartialPayment"] = postPaymentSettings.AllowPartialPayment;
                ViewData["AllowCredit"] = postPaymentSettings.AllowCredit;
                ViewData["RoundOffPayableAmount"] = postPaymentSettings.RoundOffPayableAmount;

                var postTaxContext = await LoadCreateOrderTaxContextAsync(tenantName, storeCode);
                ViewData["TaxEnabled"] = postTaxContext.EnableTax;
                ViewData["GstPercent"] = postTaxContext.TotalRatePercent;
                ViewData["PricesIncludeTax"] = postTaxContext.PricesIncludeTax;
                ViewData["TaxMode"] = postTaxContext.TaxMode;
                ViewData["TaxComponentsJson"] = JsonConvert.SerializeObject(
                    postTaxContext.Components.Select(x => new
                    {
                        code = x.Code,
                        displayName = x.DisplayName,
                        ratePercent = x.RatePercent,
                        jurisdiction = x.Jurisdiction,
                        priority = x.Priority,
                        isCompound = x.IsCompound
                    }).ToList());

                ViewData["OrderMode"] = model.OrderMode;
                ViewData["SetupOnly"] = false;
                ViewData["IsAdminUser"] = IsAdminUser();
                return View(model);
            }

            return RedirectToAction(nameof(SetupCreateOrder), new
            {
                customerName = model.CustomerName,
                customerCode = model.CustCode,
                orderMode = model.OrderMode,
                showPostActions = true
            });
        }

        [HttpGet]
        public async Task<IActionResult> PrintOrderTags(string orderNo)
        {
            var context = await TryGetOrderCompletionContextAsync(orderNo, allowFallback: true);
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                TempData["SetupCreateOrderToast"] = "Order details not available for printing tags. Please create/load the order again.";
                return RedirectToAction(nameof(SetupCreateOrder));
            }

            context.Items ??= new List<LaundryOrderItemDto>();
            await EnsureOrderTagsFromSettingsAsync(context);
            ViewData["AutoPrint"] = true;
            await LogOrderActionAuditAsync(context, "PrintTags", "Order tags printed.");
            return View(context);
        }

        [HttpGet]
        public async Task<IActionResult> PrintOrderBill(string orderNo, bool autoPrint = false, bool downloadMode = false)
        {
            var context = await TryGetOrderCompletionContextAsync(orderNo, allowFallback: true);
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                TempData["SetupCreateOrderToast"] = "Order details not available for printing bill. Please create/load the order again.";
                return RedirectToAction(nameof(SetupCreateOrder));
            }

            context.Items ??= new List<LaundryOrderItemDto>();
            ViewData["AutoPrint"] = autoPrint;
            ViewData["DownloadMode"] = downloadMode;
            await LogOrderActionAuditAsync(context, "PrintBill", autoPrint ? "Order bill auto-printed." : "Order bill opened for print.");
            return View(context);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadOrderBillPdf(string orderNo)
        {
            var context = await TryGetOrderCompletionContextAsync(orderNo, allowFallback: true);
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                TempData["SetupCreateOrderToast"] = "Order details not available for PDF download. Please create/load the order again.";
                return RedirectToAction(nameof(SetupCreateOrder));
            }

            await LogOrderActionAuditAsync(context, "DownloadBillPdf", "Order bill PDF requested from print layout.");

            return RedirectToAction(nameof(PrintOrderBill), new
            {
                orderNo = context.OrderNo,
                autoPrint = true,
                downloadMode = true
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SendOrderBillEmail(string orderNo, string email)
        {
            var context = await TryGetOrderCompletionContextAsync(orderNo, allowFallback: true);
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                return Json(new { success = false, message = "Order details not available. Please reload order and try again." });
            }

            if (string.IsNullOrWhiteSpace(context.CustomerEmail) && !string.IsNullOrWhiteSpace(context.CustomerName))
            {
                var customerProfile = await ResolveCustomerByNameAsync(context.CustomerName);
                context.CustomerEmail = customerProfile?.Email;
            }

            var toEmail = string.IsNullOrWhiteSpace(email) ? (context.CustomerEmail ?? string.Empty) : email.Trim();
            var isValidEmail = !string.IsNullOrWhiteSpace(toEmail) && new EmailAddressAttribute().IsValid(toEmail);
            if (!isValidEmail)
            {
                await LogOrderActionAuditAsync(context, "EmailBillManualSkipped", "Manual bill email skipped. Customer email missing.");
                return Json(new { success = false, message = "Customer email is missing. Please enter email and continue." });
            }

            var result = await SendOrderBillEmailInternalAsync(context, toEmail);
            if (!result.Success)
            {
                await LogOrderActionAuditAsync(context, "EmailBillManualFailed", result.Message, toEmail);
                return Json(new { success = false, message = result.Message });
            }

            context.CustomerEmail = toEmail;
            TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(context);
            await LogOrderActionAuditAsync(context, "EmailBillManual", "Bill emailed to customer.", toEmail);

            return Json(new { success = true, message = result.Message });
        }

        private async Task<OrderCompletionContextDto> TryGetOrderCompletionContextAsync(string orderNo, bool allowFallback = false)
        {
            var context = TryGetOrderCompletionContextFromTempData();
            if (context != null && string.IsNullOrWhiteSpace(orderNo))
            {
                return context;
            }

            if (context != null && !allowFallback && !string.IsNullOrWhiteSpace(context.OrderNo)
                && string.Equals(context.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase))
            {
                var hasItems = context.Items != null && context.Items.Any(x => x != null && x.Quantity > 0);
                if (hasItems || !allowFallback)
                {
                    return context;
                }
            }

            if (!allowFallback || string.IsNullOrWhiteSpace(orderNo))
            {
                return null;
            }

            var fallback = await TryBuildOrderCompletionContextFromStoreOrderAsync(orderNo.Trim());
            if (fallback != null)
            {
                fallback = await EnrichOrderCompletionContextTaxLinesAsync(fallback);
                if (!string.IsNullOrWhiteSpace(context?.CustomerEmail) && string.IsNullOrWhiteSpace(fallback.CustomerEmail))
                {
                    fallback.CustomerEmail = context.CustomerEmail;
                }

                TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(fallback);
            }

            if (fallback != null)
            {
                return fallback;
            }

            context = await EnrichOrderCompletionContextTaxLinesAsync(context);
            return context;
        }

        private static List<LaundryOrderItemDto> NormalizeOrderItems(IEnumerable<LaundryOrderItemDto> items)
        {
            return (items ?? Enumerable.Empty<LaundryOrderItemDto>())
                .Where(x => x != null)
                .Select(x => new LaundryOrderItemDto
                {
                    ServiceType = x.ServiceType,
                    Category = x.Category,
                    ItemName = x.ItemName,
                    UnitPrice = x.UnitPrice,
                    Quantity = x.Quantity > 0 ? x.Quantity : 1,
                    PieceNo = x.PieceNo,
                    TagNo = x.TagNo?.Trim()
                })
                .ToList();
        }

        private static List<LaundryOrderItemDto> ParseOrderItemsJson(string orderItemsJson)
        {
            if (string.IsNullOrWhiteSpace(orderItemsJson))
            {
                return new List<LaundryOrderItemDto>();
            }

            try
            {
                var directList = JsonConvert.DeserializeObject<List<LaundryOrderItemDto>>(orderItemsJson);
                if (directList != null && directList.Count > 0)
                {
                    return NormalizeOrderItems(directList);
                }
            }
            catch
            {
            }

            try
            {
                var token = JToken.Parse(orderItemsJson);
                var itemsToken = token;

                if (token.Type == JTokenType.Object)
                {
                    itemsToken = token["Items"]
                                 ?? token["items"]
                                 ?? token["$values"]
                                 ?? token["ResultSet"]?["Items"]
                                 ?? token["ResultSet"]?["items"]
                                 ?? token["resultSet"]?["Items"]
                                 ?? token["resultSet"]?["items"]
                                 ?? token;
                }

                var normalized = new List<LaundryOrderItemDto>();
                var array = itemsToken as JArray ?? itemsToken?["$values"] as JArray;

                if (array != null)
                {
                    foreach (var itemToken in array)
                    {
                        if (itemToken == null)
                        {
                            continue;
                        }

                        var qtyText = itemToken["Quantity"]?.ToString()
                                      ?? itemToken["quantity"]?.ToString()
                                      ?? itemToken["Qty"]?.ToString()
                                      ?? itemToken["qty"]?.ToString()
                                      ?? itemToken["Pieces"]?.ToString()
                                      ?? itemToken["pieces"]?.ToString()
                                      ?? itemToken["Piece"]?.ToString()
                                      ?? itemToken["piece"]?.ToString();

                        _ = int.TryParse(qtyText, out var qtyParsed);

                        var item = new LaundryOrderItemDto
                        {
                            ServiceType = itemToken["ServiceType"]?.ToString() ?? itemToken["serviceType"]?.ToString(),
                            Category = itemToken["Category"]?.ToString() ?? itemToken["category"]?.ToString(),
                            ItemName = itemToken["ItemName"]?.ToString() ?? itemToken["itemName"]?.ToString() ?? itemToken["Name"]?.ToString() ?? itemToken["name"]?.ToString(),
                            UnitPrice = decimal.TryParse(itemToken["UnitPrice"]?.ToString() ?? itemToken["unitPrice"]?.ToString(), out var unitPrice) ? unitPrice : 0,
                            Quantity = qtyParsed > 0 ? qtyParsed : 1,
                            PieceNo = int.TryParse(itemToken["PieceNo"]?.ToString() ?? itemToken["pieceNo"]?.ToString(), out var pieceNo) ? pieceNo : 0,
                            TagNo = (itemToken["TagNo"]?.ToString() ?? itemToken["tagNo"]?.ToString())?.Trim()
                        };

                        normalized.Add(item);
                    }
                }

                if (normalized.Count > 0)
                {
                    return NormalizeOrderItems(normalized);
                }

                var parsed = itemsToken?.ToObject<List<LaundryOrderItemDto>>() ?? new List<LaundryOrderItemDto>();
                return NormalizeOrderItems(parsed);
            }
            catch
            {
                return new List<LaundryOrderItemDto>();
            }
        }

        private OrderCompletionContextDto TryGetOrderCompletionContextFromTempData()
        {
            var payload = Convert.ToString(TempData["OrderCompletionContext"] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            TempData.Keep("OrderCompletionContext");

            try
            {
                return JsonConvert.DeserializeObject<OrderCompletionContextDto>(payload);
            }
            catch
            {
                return null;
            }
        }

        private async Task<OrderCompletionContextDto> TryBuildOrderCompletionContextFromStoreOrderAsync(string orderNo)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(orderNo))
            {
                return null;
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "status", string.Empty },
                    { "searchText", orderNo }
                }, Method.GET);

                var order = (response?.Orders ?? new List<LaundryOrderDto>())
                    .FirstOrDefault(x => x != null && string.Equals(x.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase));

                if (order == null)
                {
                    return null;
                }

                var items = NormalizeOrderItems(order.Items ?? new List<LaundryOrderItemDto>());
                if (items.Count == 0)
                {
                    items = ParseOrderItemsJson(order.OrderItemsJson);
                }

                var customerProfile = await ResolveCustomerByNameAsync(order.CustomerName);

                var resolvedTotalPieces = items.Sum(x => x?.Quantity > 0 ? x.Quantity : 0);
                if (resolvedTotalPieces <= 0)
                {
                    resolvedTotalPieces = order.TotalPieces > 0 ? order.TotalPieces : 0;
                }

                return new OrderCompletionContextDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    OrderNo = order.OrderNo,
                    InvoiceNo = string.IsNullOrWhiteSpace(order.InvoiceNo) ? order.OrderNo : order.InvoiceNo,
                    CustCode = order.CustCode,
                    CustomerName = order.CustomerName,
                    CustomerEmail = customerProfile?.Email,
                    OrderMode = order.OrderMode,
                    ServiceType = order.ServiceType,
                    OrderAmount = order.OrderAmount,
                    SubTotal = order.SubTotal,
                    TaxPercent = order.TaxPercent,
                    TaxAmount = order.TaxAmount,
                    CgstAmount = order.CgstAmount,
                    SgstAmount = order.SgstAmount,
                    TotalAmount = order.TotalAmount,
                    AdvanceUsed = order.AdvanceUsed,
                    PaidNow = order.PaidNow,
                    PendingAmount = order.PendingAmount,
                    NetPayable = order.NetPayable,
                    PaymentMode = order.PaymentMode,
                    CreatedDate = order.CreatedDate == default ? DateTime.UtcNow : order.CreatedDate,
                    TotalPieces = resolvedTotalPieces,
                    Items = items
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<LaundryOrderTaxLineDto>> TryGetOrderTaxLinesAsync(string tenantName, string storeCode, string orderNo)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(orderNo))
            {
                return new List<LaundryOrderTaxLineDto>();
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<LaundryOrderTaxLinesResponseDto>("/GetLaundryOrderTaxLines", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "orderNo", orderNo }
                }, Method.GET);

                var lines = response?.TaxLines ?? new List<LaundryOrderTaxLineDto>();
                return lines
                    .Where(x => x != null)
                    .OrderBy(x => x.LineNo)
                    .ThenBy(x => x.Priority)
                    .ThenBy(x => x.ComponentCode)
                    .ToList();
            }
            catch
            {
                return new List<LaundryOrderTaxLineDto>();
            }
        }

        private static List<LaundryOrderTaxLineDto> BuildLegacyTaxFallbackLines(OrderCompletionContextDto context)
        {
            var fallback = new List<LaundryOrderTaxLineDto>();
            if (context == null)
            {
                return fallback;
            }

            var safeTaxAmount = Math.Round(Math.Max(0m, context.TaxAmount), 2, MidpointRounding.AwayFromZero);
            if (safeTaxAmount <= 0m)
            {
                return fallback;
            }

            var cgst = Math.Round(Math.Max(0m, context.CgstAmount), 2, MidpointRounding.AwayFromZero);
            var sgst = Math.Round(Math.Max(0m, context.SgstAmount), 2, MidpointRounding.AwayFromZero);
            if (cgst > 0m || sgst > 0m)
            {
                var lineNo = 1;
                if (cgst > 0m)
                {
                    fallback.Add(new LaundryOrderTaxLineDto
                    {
                        LineNo = lineNo++,
                        TaxMode = "GST",
                        CurrencyCode = string.Empty,
                        ComponentCode = "CGST",
                        ComponentDisplayName = "CGST",
                        Priority = 1,
                        IsCompound = false,
                        RatePercent = context.TaxPercent > 0 ? Math.Round(context.TaxPercent / 2m, 2, MidpointRounding.AwayFromZero) : 0m,
                        TaxableBaseAmount = Math.Round(Math.Max(0m, context.SubTotal), 4, MidpointRounding.AwayFromZero),
                        TaxAmount = cgst
                    });
                }

                if (sgst > 0m)
                {
                    fallback.Add(new LaundryOrderTaxLineDto
                    {
                        LineNo = lineNo++,
                        TaxMode = "GST",
                        CurrencyCode = string.Empty,
                        ComponentCode = "SGST",
                        ComponentDisplayName = "SGST",
                        Priority = 2,
                        IsCompound = false,
                        RatePercent = context.TaxPercent > 0 ? Math.Round(context.TaxPercent / 2m, 2, MidpointRounding.AwayFromZero) : 0m,
                        TaxableBaseAmount = Math.Round(Math.Max(0m, context.SubTotal), 4, MidpointRounding.AwayFromZero),
                        TaxAmount = sgst
                    });
                }

                var sumLines = Math.Round(fallback.Sum(x => x.TaxAmount), 2, MidpointRounding.AwayFromZero);
                if (sumLines < safeTaxAmount && fallback.Count > 0)
                {
                    fallback[fallback.Count - 1].TaxAmount = Math.Round(fallback[fallback.Count - 1].TaxAmount + (safeTaxAmount - sumLines), 2, MidpointRounding.AwayFromZero);
                }

                return fallback;
            }

            fallback.Add(new LaundryOrderTaxLineDto
            {
                LineNo = 1,
                TaxMode = context.TaxPercent > 0 ? "GST" : "NoTax",
                CurrencyCode = string.Empty,
                ComponentCode = "TAX",
                ComponentDisplayName = context.TaxPercent > 0 ? $"Tax ({context.TaxPercent:0.##}%)" : "Tax",
                Priority = 1,
                IsCompound = false,
                RatePercent = Math.Max(0m, context.TaxPercent),
                TaxableBaseAmount = Math.Round(Math.Max(0m, context.SubTotal), 4, MidpointRounding.AwayFromZero),
                TaxAmount = safeTaxAmount
            });

            return fallback;
        }

        private static List<LaundryOrderTaxLineDto> ResolveInvoiceTaxLines(OrderCompletionContextDto context)
        {
            var snapshot = (context?.TaxLines ?? new List<LaundryOrderTaxLineDto>())
                .Where(x => x != null)
                .OrderBy(x => x.LineNo)
                .ThenBy(x => x.Priority)
                .ThenBy(x => x.ComponentCode)
                .ToList();

            if (snapshot.Count > 0)
            {
                return snapshot;
            }

            return BuildLegacyTaxFallbackLines(context);
        }

        private static decimal ResolveInvoiceTaxTotal(OrderCompletionContextDto context, IReadOnlyCollection<LaundryOrderTaxLineDto> taxLines)
        {
            var fromLines = Math.Round((taxLines ?? Array.Empty<LaundryOrderTaxLineDto>()).Where(x => x != null).Sum(x => x.TaxAmount), 2, MidpointRounding.AwayFromZero);
            if (fromLines > 0m)
            {
                return fromLines;
            }

            return Math.Round(Math.Max(0m, context?.TaxAmount ?? 0m), 2, MidpointRounding.AwayFromZero);
        }

        private async Task<OrderCompletionContextDto> EnrichOrderCompletionContextTaxLinesAsync(OrderCompletionContextDto context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.TenantName) || string.IsNullOrWhiteSpace(context.StoreCode) || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                return context;
            }

            var snapshotTaxLines = await TryGetOrderTaxLinesAsync(context.TenantName, context.StoreCode, context.OrderNo);
            context.TaxLines = snapshotTaxLines.Count > 0
                ? snapshotTaxLines
                : BuildLegacyTaxFallbackLines(context);

            return context;
        }

        private async Task<(bool Success, string Message)> SendOrderBillEmailInternalAsync(OrderCompletionContextDto context, string toEmail)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                return (false, "Order details not available. Please reload order and try again.");
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                return (false, "Customer email is missing. Please enter email and continue.");
            }

            try
            {
                toEmail = new MailAddress(toEmail.Trim()).Address;
            }
            catch
            {
                return (false, "Customer email is invalid. Please enter a valid email and continue.");
            }

            var billFromCompanyName = await GetCompanyNameAsync(context.TenantName, context.StoreCode);
            string billFromBusinessEmail = null;
            try
            {
                var storeConfig = await _storeConfigurationService.LoadAsync(context.TenantName, context.StoreCode);
                if (!string.IsNullOrWhiteSpace(storeConfig?.Email) && new EmailAddressAttribute().IsValid(storeConfig.Email))
                {
                    billFromBusinessEmail = storeConfig.Email.Trim();
                }
                else
                {
                    var tenantConfig = await _storeConfigurationService.LoadAsync(context.TenantName, string.Empty);
                    if (!string.IsNullOrWhiteSpace(tenantConfig?.Email) && new EmailAddressAttribute().IsValid(tenantConfig.Email))
                    {
                        billFromBusinessEmail = tenantConfig.Email.Trim();
                    }
                }
            }
            catch
            {
            }

            var currencyCfg = await _currencyService.GetEffectiveCurrencyAsync(HttpContext.Session.GetString("TenantName"), HttpContext.Session.GetString("TenantStore"));
            var billLines = BuildBillLines(context, currencyCfg, _currencyService, billFromCompanyName, billFromBusinessEmail);
            var body = string.Join("\n", billLines);
            var htmlBody = BuildBillHtml(context, currencyCfg, _currencyService, billFromCompanyName, billFromBusinessEmail);
            var pdfBytes = BuildStyledInvoicePdf(context, currencyCfg, _currencyService, billFromCompanyName, billFromBusinessEmail);
            var invoiceNo = string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo;

            var payload = new
            {
                TenantName = context.TenantName,
                StoreCode = context.StoreCode,
                Category = "Email",
                ProviderKey = "smtp",
                RecipientEmail = toEmail,
                Subject = $"Laundry Invoice {invoiceNo}",
                TextBody = body,
                HtmlBody = htmlBody,
                AttachmentFileName = $"Invoice-{invoiceNo}.pdf",
                AttachmentContentBase64 = Convert.ToBase64String(pdfBytes ?? Array.Empty<byte>())
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SendTenantIntegrationOrderBillEmail", payload, Method.POST);
                if (string.Equals(response?.StatusCode, "200", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "Bill emailed successfully.");
                }

                _logger.LogWarning(
                    "Order bill email failed for OrderNo {OrderNo}, Tenant {TenantName}, Store {StoreCode}, Recipient {RecipientEmail}. ApiStatus: {StatusCode}. Message: {Message}",
                    context.OrderNo,
                    context.TenantName,
                    context.StoreCode,
                    toEmail,
                    response?.StatusCode,
                    response?.Message);

                return (false, string.IsNullOrWhiteSpace(response?.Message) ? "Unable to send bill email." : response.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Order bill email exception for OrderNo {OrderNo}, Tenant {TenantName}, Store {StoreCode}, Recipient {RecipientEmail}",
                    context.OrderNo,
                    context.TenantName,
                    context.StoreCode,
                    toEmail);

                return (false, BuildApiErrorMessage(ex, "Unable to send bill email."));
            }
        }

        private async Task LogOrderActionAuditAsync(OrderCompletionContextDto context, string actionName, string actionMessage = null, string customerEmail = null)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                return;
            }

            await LogOrderActionAuditAsync(
                context.TenantName,
                context.StoreCode,
                context.OrderNo,
                context.InvoiceNo,
                actionName,
                actionMessage,
                customerEmail ?? context.CustomerEmail);
        }

        private async Task LogOrderActionAuditAsync(string tenantName, string storeCode, string orderNo, string invoiceNo, string actionName, string actionMessage = null, string customerEmail = null)
        {
            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(orderNo)
                || string.IsNullOrWhiteSpace(actionName))
            {
                return;
            }

            try
            {
                var actorEmail = HttpContext.Session.GetString("TenantName");
                await _dbContext.OrderActionAuditLogs.AddAsync(new OrderActionAuditLog
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    OrderNo = orderNo,
                    InvoiceNo = string.IsNullOrWhiteSpace(invoiceNo) ? orderNo : invoiceNo,
                    ActionName = actionName,
                    ActionMessage = actionMessage,
                    CustomerEmail = customerEmail,
                    UserEmail = actorEmail,
                    LoggedAtUtc = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var sqlEx = TryGetSqlException(ex);
                if (sqlEx != null)
                {
                    _logger?.LogError(
                        ex,
                        "OrderActionAudit log insert failed. Tenant={TenantName}, Store={StoreCode}, OrderNo={OrderNo}, ActionName={ActionName}, SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                        tenantName,
                        storeCode,
                        orderNo,
                        actionName,
                        sqlEx.Number,
                        sqlEx.State,
                        sqlEx.Class,
                        sqlEx.Procedure ?? string.Empty,
                        sqlEx.LineNumber,
                        sqlEx.Server ?? string.Empty,
                        sqlEx.Message);
                }
                else
                {
                    _logger?.LogError(
                        ex,
                        "OrderActionAudit log insert failed. Tenant={TenantName}, Store={StoreCode}, OrderNo={OrderNo}, ActionName={ActionName}",
                        tenantName,
                        storeCode,
                        orderNo,
                        actionName);
                }

                // Do not block order flow when audit insert fails.
            }
        }

        private static (decimal OrderAmount, decimal SubTotal, decimal TaxAmount, decimal TotalAmount, decimal AdvanceUsed, decimal PaidNow, decimal PendingAmount, decimal NetPayable)
            ResolveOrderFinancialSummary(decimal orderAmount, decimal subTotal, decimal taxAmount, decimal totalAmount, decimal advanceUsed, decimal paidNow, decimal pendingAmount, decimal netPayable)
        {
            var safeOrderAmount = Math.Round(Math.Max(0m, orderAmount), 2, MidpointRounding.AwayFromZero);
            var safeSubTotal = Math.Round(Math.Max(0m, subTotal), 2, MidpointRounding.AwayFromZero);
            if (safeSubTotal <= 0m)
            {
                safeSubTotal = safeOrderAmount;
            }

            var safeTaxAmount = Math.Round(Math.Max(0m, taxAmount), 2, MidpointRounding.AwayFromZero);
            var safeTotalAmount = Math.Round(Math.Max(0m, totalAmount), 2, MidpointRounding.AwayFromZero);
            if (safeTotalAmount <= 0m)
            {
                safeTotalAmount = Math.Round(Math.Max(0m, safeSubTotal + safeTaxAmount), 2, MidpointRounding.AwayFromZero);
            }

            if (safeTaxAmount <= 0m && safeTotalAmount > safeSubTotal)
            {
                safeTaxAmount = Math.Round(safeTotalAmount - safeSubTotal, 2, MidpointRounding.AwayFromZero);
            }

            var safeAdvanceUsed = Math.Round(Math.Max(0m, advanceUsed), 2, MidpointRounding.AwayFromZero);
            if (safeAdvanceUsed > safeTotalAmount)
            {
                safeAdvanceUsed = safeTotalAmount;
            }

            var payableAfterAdvance = Math.Round(Math.Max(0m, safeTotalAmount - safeAdvanceUsed), 2, MidpointRounding.AwayFromZero);
            var safePaidNow = Math.Round(Math.Max(0m, paidNow), 2, MidpointRounding.AwayFromZero);
            if (safePaidNow > payableAfterAdvance)
            {
                safePaidNow = payableAfterAdvance;
            }

            var computedPending = Math.Round(Math.Max(0m, payableAfterAdvance - safePaidNow), 2, MidpointRounding.AwayFromZero);
            var safePendingAmount = Math.Round(Math.Max(0m, pendingAmount), 2, MidpointRounding.AwayFromZero);
            var safeNetPayable = Math.Round(Math.Max(0m, netPayable), 2, MidpointRounding.AwayFromZero);

            if (safePendingAmount <= 0m && computedPending > 0m)
            {
                safePendingAmount = computedPending;
            }

            if (safeNetPayable <= 0m)
            {
                safeNetPayable = safePendingAmount > 0m ? safePendingAmount : computedPending;
            }

            if (safePendingAmount <= 0m)
            {
                safePendingAmount = safeNetPayable;
            }

            return (safeOrderAmount, safeSubTotal, safeTaxAmount, safeTotalAmount, safeAdvanceUsed, safePaidNow, safePendingAmount, safeNetPayable);
        }

        private static List<string> BuildBillLines(OrderCompletionContextDto context, LMSWebUI.Services.CurrencyConfig currencyCfg, LMSWebUI.Services.ICurrencyService currencyService, string billFromCompanyName, string billFromBusinessEmail)
        {
            var resolvedCompanyName = string.IsNullOrWhiteSpace(billFromCompanyName) ? "Laundry Service" : billFromCompanyName.Trim();
            var summary = ResolveOrderFinancialSummary(
                context?.OrderAmount ?? 0m,
                context?.SubTotal ?? 0m,
                context?.TaxAmount ?? 0m,
                context?.TotalAmount ?? 0m,
                context?.AdvanceUsed ?? 0m,
                context?.PaidNow ?? 0m,
                context?.PendingAmount ?? 0m,
                context?.NetPayable ?? 0m);
            var taxLines = ResolveInvoiceTaxLines(context);
            var resolvedTaxTotal = ResolveInvoiceTaxTotal(context, taxLines);
            var lines = new List<string>
            {
                $"Bill From: {resolvedCompanyName}",
                $"Store: {context.StoreCode}",
                $"Invoice No: {(string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo)}",
                $"Order No: {context.OrderNo}",
                $"Customer: {context.CustomerName} ({context.CustCode})",
                $"Date (UTC): {(context.CreatedDate == default ? DateTime.UtcNow : context.CreatedDate):yyyy-MM-dd HH:mm}",
                "",
                "Items:"
            };

            if (!string.IsNullOrWhiteSpace(billFromBusinessEmail))
            {
                lines.Insert(1, $"From Email: {billFromBusinessEmail.Trim()}");
            }

                if (context.Items != null && context.Items.Count > 0)
            {
                var idx = 1;
                foreach (var item in context.Items)
                {
                        var unitPriceText = currencyService != null && currencyCfg != null
                            ? currencyService.Format(item.UnitPrice, currencyCfg)
                            : item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
                        lines.Add($"{idx}. {item.ItemName}  Qty:{item.Quantity}  Unit:{unitPriceText}  Tag:{item.TagNo}");
                    idx++;
                }
            }
            else
            {
                lines.Add("No item lines available.");
            }

            lines.Add("");
            lines.Add($"Service: {context.ServiceType}");
            lines.Add($"Order Mode: {context.OrderMode}");
            if (currencyService != null && currencyCfg != null)
            {
                lines.Add($"Subtotal: {currencyService.Format(summary.SubTotal, currencyCfg)}");
                if (taxLines.Count > 0)
                {
                    foreach (var taxLine in taxLines)
                    {
                        var label = string.IsNullOrWhiteSpace(taxLine.ComponentDisplayName) ? (taxLine.ComponentCode ?? "Tax") : taxLine.ComponentDisplayName;
                        lines.Add($"{label}: {currencyService.Format(taxLine.TaxAmount, currencyCfg)}");
                    }
                }
                lines.Add($"Total Tax: {currencyService.Format(resolvedTaxTotal, currencyCfg)}");
                lines.Add($"Total: {currencyService.Format(summary.TotalAmount, currencyCfg)}");
                lines.Add($"Advance Utilized (-): {currencyService.Format(summary.AdvanceUsed, currencyCfg)}");
                lines.Add($"Paid Now (-): {currencyService.Format(summary.PaidNow, currencyCfg)}");
                lines.Add($"Pending Amount: {currencyService.Format(summary.PendingAmount, currencyCfg)}");
                lines.Add($"Net Payable: {currencyService.Format(summary.NetPayable, currencyCfg)}");
            }
            else
            {
                lines.Add($"Subtotal: {summary.SubTotal:0.00}");
                if (taxLines.Count > 0)
                {
                    foreach (var taxLine in taxLines)
                    {
                        var label = string.IsNullOrWhiteSpace(taxLine.ComponentDisplayName) ? (taxLine.ComponentCode ?? "Tax") : taxLine.ComponentDisplayName;
                        lines.Add($"{label}: {taxLine.TaxAmount:0.00}");
                    }
                }
                lines.Add($"Total Tax: {resolvedTaxTotal:0.00}");
                lines.Add($"Total: {summary.TotalAmount:0.00}");
                lines.Add($"Advance Utilized (-): {summary.AdvanceUsed:0.00}");
                lines.Add($"Paid Now (-): {summary.PaidNow:0.00}");
                lines.Add($"Pending Amount: {summary.PendingAmount:0.00}");
                lines.Add($"Net Payable: {summary.NetPayable:0.00}");
            }

            return lines;
        }

        private static string BuildBillHtml(OrderCompletionContextDto context, LMSWebUI.Services.CurrencyConfig currencyCfg, LMSWebUI.Services.ICurrencyService currencyService, string billFromCompanyName, string billFromBusinessEmail)
        {
            context ??= new OrderCompletionContextDto();
            context.Items ??= new List<LaundryOrderItemDto>();

            var invoiceNo = string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo;
            var issueDate = context.CreatedDate == default ? DateTime.UtcNow : context.CreatedDate;
            var companyName = string.IsNullOrWhiteSpace(billFromCompanyName) ? "Laundry Service" : billFromCompanyName.Trim();
            var summary = ResolveOrderFinancialSummary(context.OrderAmount, context.SubTotal, context.TaxAmount, context.TotalAmount, context.AdvanceUsed, context.PaidNow, context.PendingAmount, context.NetPayable);
            var taxLines = ResolveInvoiceTaxLines(context);
            var resolvedTaxTotal = ResolveInvoiceTaxTotal(context, taxLines);

            var pendingAmount = summary.PendingAmount;
            var isPaid = pendingAmount <= 0;
            var statusText = isPaid ? "PAID IN FULL" : "BALANCE DUE";
            var statusBg = isPaid ? "#dcfce7" : "#fee2e2";
            var statusFg = isPaid ? "#166534" : "#991b1b";
            var statusBd = isPaid ? "#86efac" : "#fca5a5";

            string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
            string F(decimal value)
                => WebUtility.HtmlEncode(currencyService != null && currencyCfg != null
                    ? currencyService.Format(value, currencyCfg)
                    : value.ToString("0.00", CultureInfo.InvariantCulture));

            var fromEmailLine = string.IsNullOrWhiteSpace(billFromBusinessEmail)
                ? string.Empty
                : $"<div style='font-size:12px;color:#6b7280;margin-top:2px;'>From Email: {E(billFromBusinessEmail.Trim())}</div>";

            var rows = new StringBuilder();
            if (context.Items.Count > 0)
            {
                foreach (var item in context.Items)
                {
                    var itemName = E(item?.ItemName);
                    var tag = E(item?.TagNo);
                    var qty = item?.Quantity > 0 ? item.Quantity : 1;
                    var lineTotal = (item?.UnitPrice ?? 0) * qty;

                    rows.Append("<tr>")
                        .Append($"<td style='padding:8px;border-bottom:1px solid #e5e7eb;'>{itemName} {(string.IsNullOrWhiteSpace(tag) ? string.Empty : "&middot; " + tag)}</td>")
                        .Append($"<td style='padding:8px;border-bottom:1px solid #e5e7eb;text-align:center;'>{qty}</td>")
                        .Append($"<td style='padding:8px;border-bottom:1px solid #e5e7eb;text-align:right;'>{F(lineTotal)}</td>")
                        .Append("</tr>");
                }
            }
            else
            {
                rows.Append("<tr><td colspan='3' style='padding:10px;color:#6b7280;'>No item lines available.</td></tr>");
            }

            var taxRows = new StringBuilder();
            if (taxLines.Count > 0)
            {
                foreach (var taxLine in taxLines)
                {
                    var label = E(string.IsNullOrWhiteSpace(taxLine.ComponentDisplayName) ? (taxLine.ComponentCode ?? "Tax") : taxLine.ComponentDisplayName);
                    taxRows.Append($"<tr><td style='padding:5px 0;color:#6b7280;'>{label}</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(taxLine.TaxAmount)}</td></tr>");
                }
            }
            taxRows.Append($"<tr><td style='padding:5px 0;color:#6b7280;'>Total Tax</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(resolvedTaxTotal)}</td></tr>");

            return $@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width,initial-scale=1' />
</head>
<body style='margin:0;padding:0;background:#f3f6fb;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>
  <div style='max-width:680px;margin:0 auto;padding:16px;'>
    <div style='background:#ffffff;border:1px solid #dbe3ec;border-radius:10px;padding:18px;'>
      <div style='display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap;margin-bottom:12px;'>
        <div>
          <div style='font-size:20px;font-weight:800;color:#1f4e79;'>{E(companyName)}</div>
          <div style='font-size:12px;color:#6b7280;'>Laundry Service &middot; Store {E(context.StoreCode)}</div>
          {fromEmailLine}
        </div>
        <span style='display:inline-block;padding:6px 12px;border-radius:999px;border:1px solid {statusBd};background:{statusBg};color:{statusFg};font-size:12px;font-weight:800;letter-spacing:.4px;'>{statusText}</span>
      </div>

      <div style='font-size:22px;font-weight:800;color:#0f172a;margin:2px 0 10px;'>Invoice Confirmation</div>
      <div style='font-size:14px;color:#374151;line-height:1.5;margin-bottom:14px;'>
        Thank you, {E(context.CustomerName)}, for choosing our laundry service. Your order has been received{(isPaid ? " and payment is complete." : ".")}
      </div>

      <table role='presentation' style='width:100%;border-collapse:collapse;font-size:13px;margin-bottom:12px;'>
        <tr>
          <td style='padding:6px 0;color:#6b7280;width:38%;'>Invoice No.</td><td style='padding:6px 0;font-weight:700;'>{E(invoiceNo)}</td>
        </tr>
        <tr>
          <td style='padding:6px 0;color:#6b7280;'>Order No.</td><td style='padding:6px 0;font-weight:700;'>{E(context.OrderNo)}</td>
        </tr>
        <tr>
          <td style='padding:6px 0;color:#6b7280;'>Customer Code</td><td style='padding:6px 0;font-weight:700;'>{E(context.CustCode)}</td>
        </tr>
        <tr>
          <td style='padding:6px 0;color:#6b7280;'>Order Date (UTC)</td><td style='padding:6px 0;font-weight:700;'>{E(issueDate.ToString("dd MMM yyyy, HH:mm"))}</td>
        </tr>
        <tr>
          <td style='padding:6px 0;color:#6b7280;'>Service</td><td style='padding:6px 0;font-weight:700;'>{E(context.ServiceType)}</td>
        </tr>
        <tr>
          <td style='padding:6px 0;color:#6b7280;'>Order Mode</td><td style='padding:6px 0;font-weight:700;'>{E(context.OrderMode)}</td>
        </tr>
      </table>

      <div style='font-size:14px;font-weight:700;color:#0f172a;margin:8px 0;'>Order details</div>
      <table role='presentation' style='width:100%;border-collapse:collapse;border:1px solid #dbe3ec;border-radius:8px;overflow:hidden;font-size:13px;'>
        <thead>
          <tr style='background:#eef4fc;'>
            <th style='text-align:left;padding:8px;border-bottom:1px solid #dbe3ec;'>Item / Tag</th>
            <th style='text-align:center;padding:8px;border-bottom:1px solid #dbe3ec;width:70px;'>Qty</th>
            <th style='text-align:right;padding:8px;border-bottom:1px solid #dbe3ec;width:120px;'>Amount</th>
          </tr>
        </thead>
        <tbody>
          {rows}
        </tbody>
      </table>

      <table role='presentation' style='width:100%;border-collapse:collapse;font-size:13px;margin-top:12px;'>
        <tr><td style='padding:5px 0;color:#6b7280;'>Subtotal</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(summary.SubTotal)}</td></tr>
        {taxRows}
        <tr><td style='padding:5px 0;color:#6b7280;'>Total</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(summary.TotalAmount)}</td></tr>
        <tr><td style='padding:5px 0;color:#6b7280;'>Advance utilized</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(summary.AdvanceUsed)}</td></tr>
        <tr><td style='padding:5px 0;color:#6b7280;'>Paid now</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(summary.PaidNow)}</td></tr>
        <tr><td style='padding:5px 0;color:#6b7280;'>Pending amount</td><td style='padding:5px 0;text-align:right;font-weight:700;'>{F(summary.PendingAmount)}</td></tr>
        <tr><td style='padding-top:8px;border-top:1px solid #dbe3ec;font-size:15px;font-weight:800;'>Net payable</td><td style='padding-top:8px;border-top:1px solid #dbe3ec;text-align:right;font-size:15px;font-weight:800;'>{F(summary.NetPayable)}</td></tr>
      </table>

      <div style='margin-top:14px;font-size:13px;color:#374151;'>
        {(isPaid ? "Payment received in full." : "Payment pending. Please settle the balance due.")}
      </div>
      <div style='margin-top:8px;font-size:13px;color:#374151;'>Thank you for your business!</div>
      <div style='margin-top:2px;font-size:12px;color:#6b7280;'>Please keep this email for your records. Contact the store if you have any questions.</div>
    </div>
  </div>
</body>
</html>";
        }

        private static byte[] BuildStyledInvoicePdf(OrderCompletionContextDto context, LMSWebUI.Services.CurrencyConfig currencyCfg, LMSWebUI.Services.ICurrencyService currencyService, string billFromCompanyName, string billFromBusinessEmail)
        {
            context ??= new OrderCompletionContextDto();
            context.Items ??= new List<LaundryOrderItemDto>();

            var invoiceNo = string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo;
            var issueDate = context.CreatedDate == default ? DateTime.UtcNow : context.CreatedDate;
            var dueDate = issueDate;
            var summary = ResolveOrderFinancialSummary(context.OrderAmount, context.SubTotal, context.TaxAmount, context.TotalAmount, context.AdvanceUsed, context.PaidNow, context.PendingAmount, context.NetPayable);
            var subTotal = summary.SubTotal;
            var taxLines = ResolveInvoiceTaxLines(context);
            var tax = ResolveInvoiceTaxTotal(context, taxLines);
            var advanceUsed = summary.AdvanceUsed;
            var totalDue = summary.NetPayable;

            var companyName = string.IsNullOrWhiteSpace(billFromCompanyName)
                ? "Laundry Service"
                : billFromCompanyName.Trim();

            QuestPDF.Settings.License = LicenseType.Community;
            string FormatAmount(decimal value, LMSWebUI.Services.CurrencyConfig cfg)
            {
                return CurrencyFormatting.FormatAmount(value, cfg, currencyService);
            }

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("INVOICE").FontSize(24).SemiBold().FontColor("#0f172a");
                            row.RelativeItem().AlignRight().Column(right =>
                            {
                                right.Item().Text(companyName).SemiBold().FontColor("#1f4e79");
                                right.Item().Text($"Store: {context.StoreCode}");
                                right.Item().Text($"Invoice No: {invoiceNo}");
                                right.Item().Text($"Order No: {context.OrderNo}");
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor("#1f4e79");

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Border(1).BorderColor("#dbe3ec").Padding(8).Column(c =>
                            {
                                c.Item().Text("BILL FROM").FontSize(8).SemiBold().FontColor("#6b7280");
                                c.Item().Text(companyName).SemiBold();
                                if (!string.IsNullOrWhiteSpace(billFromBusinessEmail))
                                {
                                    c.Item().Text($"From Email: {billFromBusinessEmail.Trim()}");
                                }
                                c.Item().Text($"Store: {context.StoreCode}");
                                c.Item().Text($"Service: {context.ServiceType}");
                            });

                            row.RelativeItem().Border(1).BorderColor("#dbe3ec").Padding(8).Column(c =>
                            {
                                c.Item().Text("BILL TO").FontSize(8).SemiBold().FontColor("#6b7280");
                                c.Item().Text(context.CustomerName ?? string.Empty).SemiBold();
                                c.Item().Text($"Customer Code: {context.CustCode}");
                                c.Item().Text($"Order Mode: {context.OrderMode}");
                            });

                            row.RelativeItem().Border(1).BorderColor("#dbe3ec").Padding(8).Column(c =>
                            {
                                c.Item().Text($"ISSUE DATE: {issueDate:dd/MM/yyyy}").SemiBold();
                                c.Item().PaddingTop(4).Text($"DUE DATE: {dueDate:dd/MM/yyyy}").SemiBold();
                            });
                        });

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(1.2f);
                                columns.RelativeColumn(1f);
                                columns.RelativeColumn(1.2f);
                            });

                            table.Header(header =>
                            {
                                void headerCell(string text, bool right = false)
                                {
                                    var cell = header.Cell().Background("#eaf2fb").Border(1).BorderColor("#dbe3ec").Padding(6);
                                    if (right)
                                    {
                                        cell.AlignRight().Text(text).SemiBold();
                                    }
                                    else
                                    {
                                        cell.Text(text).SemiBold();
                                    }
                                }

                                headerCell("Description");
                                headerCell("Price", true);
                                headerCell("QTY", true);
                                headerCell("Total", true);
                            });

                            if (context.Items.Count > 0)
                            {
                                foreach (var item in context.Items)
                                {
                                    var lineTotal = item.UnitPrice * item.Quantity;
                                    var desc = string.IsNullOrWhiteSpace(item.TagNo)
                                        ? item.ItemName
                                        : $"{item.ItemName} (Tag: {item.TagNo})";

                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).Text(desc ?? string.Empty);
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(FormatAmount(item.UnitPrice, currencyCfg));
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(item.Quantity.ToString());
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(FormatAmount(lineTotal, currencyCfg));
                                }
                            }
                            else
                            {
                                table.Cell().ColumnSpan(4).BorderBottom(1).BorderColor("#edf1f6").Padding(6).Text("No item lines available.");
                            }
                        });

                        col.Item().AlignRight().Width(240).Border(1).BorderColor("#dbe3ec").Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            void sumRow(string label, string value, bool highlight = false)
                            {
                                if (highlight)
                                {
                                    table.Cell().Padding(6).Background("#eaf2fb").Text(label).SemiBold();
                                    table.Cell().Padding(6).Background("#eaf2fb").AlignRight().Text(value).SemiBold();
                                }
                                else
                                {
                                    table.Cell().Padding(6).Background("#ffffff").Text(label);
                                    table.Cell().Padding(6).Background("#ffffff").AlignRight().Text(value);
                                }
                            }

                            sumRow("Subtotal", FormatAmount(subTotal, currencyCfg));
                            if (taxLines.Count > 0)
                            {
                                foreach (var taxLine in taxLines)
                                {
                                    var label = string.IsNullOrWhiteSpace(taxLine.ComponentDisplayName) ? (taxLine.ComponentCode ?? "Tax") : taxLine.ComponentDisplayName;
                                    sumRow(label, FormatAmount(taxLine.TaxAmount, currencyCfg));
                                }
                            }
                            sumRow("Total Tax", FormatAmount(tax, currencyCfg));
                            sumRow("Total", FormatAmount(summary.TotalAmount, currencyCfg));
                            sumRow("Advance Utilized (-)", FormatAmount(advanceUsed, currencyCfg));
                            sumRow("Paid Now (-)", FormatAmount(summary.PaidNow, currencyCfg));
                            sumRow("Pending Amount", FormatAmount(summary.PendingAmount, currencyCfg));
                            sumRow("Total Due", FormatAmount(totalDue, currencyCfg), true);
                        });

                        col.Item().Border(1).BorderColor("#dbe3ec").Padding(8).Text($"Payment Information: {context.OrderMode} / {context.ServiceType} / Advance Used: {FormatAmount(summary.AdvanceUsed, currencyCfg)} / Paid Now: {FormatAmount(summary.PaidNow, currencyCfg)} / Pending: {FormatAmount(summary.PendingAmount, currencyCfg)}");
                        col.Item().AlignCenter().Text("Thank you for your business!").FontColor("#1f4e79").SemiBold();
                    });
                });
            }).GeneratePdf();
        }

        private static byte[] BuildSimplePdf(List<string> lines)
        {
            var safeLines = (lines ?? new List<string>()).Select(l => EscapePdfText(l)).ToList();

            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine("BT");
            contentBuilder.AppendLine("/F1 11 Tf");
            contentBuilder.AppendLine("50 790 Td");

            for (var i = 0; i < safeLines.Count; i++)
            {
                if (i > 0)
                {
                    contentBuilder.AppendLine("0 -15 Td");
                }
                contentBuilder.AppendLine($"({safeLines[i]}) Tj");
            }

            contentBuilder.AppendLine("ET");
            var content = contentBuilder.ToString();

            var objects = new List<string>
            {
                "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
                "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
                "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >> endobj",
                "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj",
                $"5 0 obj << /Length {Encoding.ASCII.GetByteCount(content)} >> stream\n{content}endstream\nendobj"
            };

            var sb = new StringBuilder();
            sb.AppendLine("%PDF-1.4");
            var offsets = new List<int> { 0 };

            foreach (var obj in objects)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
                sb.AppendLine(obj);
            }

            var xrefPos = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.AppendLine("xref");
            sb.AppendLine($"0 {objects.Count + 1}");
            sb.AppendLine("0000000000 65535 f ");
            for (var i = 1; i <= objects.Count; i++)
            {
                sb.AppendLine($"{offsets[i].ToString("D10")} 00000 n ");
            }

            sb.AppendLine("trailer");
            sb.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
            sb.AppendLine("startxref");
            sb.AppendLine(xrefPos.ToString());
            sb.AppendLine("%%EOF");

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string EscapePdfText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        [HttpGet]
        public async Task<JsonResult> GetCustomerOrders(string customerName, string customerCode = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");
            var resolvedCustomerCode = customerCode?.Trim();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            customerName = customerName?.Trim();
            customerCode = customerCode?.Trim();
            resolvedCustomerCode = customerCode;

            if (string.IsNullOrWhiteSpace(customerName) && string.IsNullOrWhiteSpace(customerCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Please enter customer name.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(resolvedCustomerCode))
                {
                    var customer = await ResolveCustomerByNameAsync(customerName);

                    if (customer == null || customer.StatusCode != "200" || string.IsNullOrWhiteSpace(customer.CustCode))
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Customer not found.",
                            orders = new List<LaundryOrderDto>()
                        });
                    }

                    resolvedCustomerCode = customer.CustCode?.Trim();
                }

                var response = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "custCode", resolvedCustomerCode }
                }, Method.GET);

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    customerCode = resolvedCustomerCode,
                    orders = response?.Orders ?? new List<LaundryOrderDto>()
                });
            }
            catch (Exception ex)
            {
                var rawMessage = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
                if (rawMessage.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                    || rawMessage.Contains("404", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        customerCode = resolvedCustomerCode,
                        orders = new List<LaundryOrderDto>()
                    });
                }

                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to fetch orders."),
                    orders = new List<LaundryOrderDto>()
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> SearchOrderReference(string searchText)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again."
                });
            }

            var term = searchText?.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                return Json(new
                {
                    success = false,
                    message = "Please enter Order No or Invoice No."
                });
            }

            var termLower = term.ToLowerInvariant();

            try
            {
                var ordersResponse = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "status", string.Empty },
                    { "searchText", term }
                }, Method.GET);

                var orders = (ordersResponse?.Orders ?? new List<LaundryOrderDto>())
                    .OrderByDescending(x => x.CreatedDate)
                    .ToList();

                var exactMatch = orders.FirstOrDefault(o =>
                    (!string.IsNullOrWhiteSpace(o.OrderNo) && string.Equals(o.OrderNo.Trim(), term, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(o.InvoiceNo) && string.Equals(o.InvoiceNo.Trim(), term, StringComparison.OrdinalIgnoreCase)));

                var startsWithMatch = exactMatch ?? orders.FirstOrDefault(o =>
                    (!string.IsNullOrWhiteSpace(o.OrderNo) && o.OrderNo.Trim().ToLowerInvariant().StartsWith(termLower))
                    || (!string.IsNullOrWhiteSpace(o.InvoiceNo) && o.InvoiceNo.Trim().ToLowerInvariant().StartsWith(termLower)));

                var containsMatch = startsWithMatch ?? orders.FirstOrDefault(o =>
                    (!string.IsNullOrWhiteSpace(o.OrderNo) && o.OrderNo.Trim().ToLowerInvariant().Contains(termLower))
                    || (!string.IsNullOrWhiteSpace(o.InvoiceNo) && o.InvoiceNo.Trim().ToLowerInvariant().Contains(termLower)));

                var match = containsMatch;

                if (match != null)
                {
                    return Json(new
                    {
                        success = true,
                        message = string.Empty,
                        customerName = match.CustomerName,
                        customerCode = match.CustCode,
                        orderNo = match.OrderNo,
                        invoiceNo = match.InvoiceNo,
                        orderMode = string.IsNullOrWhiteSpace(match.OrderMode) ? "pieces" : match.OrderMode
                    });
                }

                return Json(new
                {
                    success = false,
                    message = "No order found for the given Order No / Invoice No in this store."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to search order reference.")
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> SearchOrderReferences(string searchText, int top = 20)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            var term = searchText?.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                return Json(new List<string>());
            }

            var maxRows = top < 1 ? 20 : (top > 50 ? 50 : top);
            var termLower = term.ToLowerInvariant();

            try
            {
                var results = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var ordersResponse = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "status", string.Empty },
                    { "searchText", term }
                }, Method.GET);

                var orders = (ordersResponse?.Orders ?? new List<LaundryOrderDto>())
                    .OrderByDescending(x => x.CreatedDate)
                    .ToList();

                foreach (var order in orders)
                {
                    var orderNo = order.OrderNo?.Trim();
                    var invoiceNo = order.InvoiceNo?.Trim();

                    if (!string.IsNullOrWhiteSpace(orderNo) && orderNo.ToLowerInvariant().StartsWith(termLower) && seen.Add(orderNo))
                    {
                        results.Add(orderNo);
                        if (results.Count >= maxRows)
                        {
                            return Json(results);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(invoiceNo) && invoiceNo.ToLowerInvariant().StartsWith(termLower) && seen.Add(invoiceNo))
                    {
                        results.Add(invoiceNo);
                        if (results.Count >= maxRows)
                        {
                            return Json(results);
                        }
                    }
                }

                if (results.Count == 0)
                {
                    foreach (var order in orders)
                    {
                        var orderNo = order.OrderNo?.Trim();
                        var invoiceNo = order.InvoiceNo?.Trim();

                        if (!string.IsNullOrWhiteSpace(orderNo) && orderNo.ToLowerInvariant().Contains(termLower) && seen.Add(orderNo))
                        {
                            results.Add(orderNo);
                            if (results.Count >= maxRows)
                            {
                                return Json(results);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(invoiceNo) && invoiceNo.ToLowerInvariant().Contains(termLower) && seen.Add(invoiceNo))
                        {
                            results.Add(invoiceNo);
                            if (results.Count >= maxRows)
                            {
                                return Json(results);
                            }
                        }
                    }
                }

                return Json(results);
            }
            catch
            {
                return Json(new List<string>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> OrderWorkflow()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Failed.",
                    DisplayMessage = "Session expired. Please log in again."
                });
                return RedirectToAction("Login", "Login");
            }

            if (!CanManageStoreOrderWorkflow())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin and store users can manage order workflow."
                });
                return RedirectToAction("Index", "Dashboard");
            }

            var workflowSettings = await LoadWorkflowStatusSettingsAsync(tenantName, storeCode);
            var activeStatuses = (workflowSettings?.Statuses ?? WorkflowStatusSettingsViewModel.GetSeedStatuses())
                .Where(x => x != null && x.IsActive && !string.IsNullOrWhiteSpace(x.StatusName))
                .OrderBy(x => x.SortOrder)
                .Select(x => new
                {
                    name = x.StatusName?.Trim(),
                    color = string.IsNullOrWhiteSpace(x.ColorCode) ? "#6f7a86" : x.ColorCode.Trim(),
                    isFinal = x.IsFinal,
                    sortOrder = x.SortOrder
                })
                .ToList();

            ViewData["WorkflowTenantName"] = tenantName;
            ViewData["WorkflowStoreCode"] = storeCode;
            ViewData["WorkflowStatusesJson"] = JsonConvert.SerializeObject(activeStatuses);
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetStoreWorkflowOrders(string status = null, string searchText = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            if (!CanManageStoreOrderWorkflow())
            {
                return Json(new
                {
                    success = false,
                    message = "Only admin and store users can manage order workflow.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            try
            {
                _logger.LogInformation(
                    "GetStoreWorkflowOrders started. TenantName={TenantName}, StoreCode={StoreCode}, StatusFilter={StatusFilter}, SearchTextLength={SearchTextLength}",
                    tenantName,
                    storeCode,
                    status ?? string.Empty,
                    searchText?.Length ?? 0);

                var response = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "status", status ?? string.Empty },
                    { "searchText", searchText ?? string.Empty }
                }, Method.GET);

                _logger.LogInformation(
                    "GetStoreWorkflowOrders downstream call completed. DownstreamHttpStatusCode={DownstreamHttpStatusCode}, ResponseStatusCodeField={ResponseStatusCode}, OrderCount={OrderCount}",
                    "NotAvailable",
                    response?.StatusCode ?? string.Empty,
                    response?.Orders?.Count ?? 0);

                var orders = response?.Orders ?? new List<LaundryOrderDto>();

                var normalizedSearchText = searchText?.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedSearchText) && orders.Count > 1)
                {
                    var exactMatches = orders
                        .Where(x => x != null &&
                            (
                                string.Equals((x.OrderNo ?? string.Empty).Trim(), normalizedSearchText, StringComparison.OrdinalIgnoreCase)
                                || string.Equals((x.InvoiceNo ?? string.Empty).Trim(), normalizedSearchText, StringComparison.OrdinalIgnoreCase)
                            ))
                        .ToList();

                    if (exactMatches.Count > 0)
                    {
                        orders = exactMatches;
                    }
                }

                foreach (var order in orders)
                {
                    order.Status = NormalizeWorkflowStatus(order.Status);
                    var existingTotalPieces = order.TotalPieces;

                    var orderItems = NormalizeOrderItems(order.Items ?? new List<LaundryOrderItemDto>());
                    if (orderItems.Count == 0)
                    {
                        orderItems = ParseOrderItemsJson(order.OrderItemsJson);
                    }

                    var resolvedTotalPieces = orderItems.Sum(x => x?.Quantity > 0 ? x.Quantity : 0);
                    if (resolvedTotalPieces <= 0)
                    {
                        resolvedTotalPieces = existingTotalPieces > 0 ? existingTotalPieces : 0;
                    }

                    order.TotalPieces = resolvedTotalPieces;
                }

                var counters = new
                {
                    total = orders.Count,
                    received = orders.Count(x => string.Equals(x.Status, "Received", StringComparison.OrdinalIgnoreCase)),
                    processing = orders.Count(x => string.Equals(x.Status, "Processing", StringComparison.OrdinalIgnoreCase)),
                    ready = orders.Count(x => string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase)),
                    delivered = orders.Count(x => string.Equals(x.Status, "Delivered", StringComparison.OrdinalIgnoreCase))
                };

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    tenantName,
                    storeCode,
                    counters,
                    orders
                });
            }
            catch (ApiUnavailableException apiEx)
            {
                _logger.LogError(
                    apiEx,
                    "GetStoreWorkflowOrders downstream API failed. DownstreamHttpStatusCode={DownstreamHttpStatusCode}, Message={Message}",
                    apiEx.StatusCode?.ToString() ?? "NotAvailable",
                    apiEx.Message);

                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(apiEx, "Unable to load workflow orders."),
                    orders = new List<LaundryOrderDto>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GetStoreWorkflowOrders failed. DownstreamHttpStatusCode={DownstreamHttpStatusCode}, Message={Message}",
                    "NotAvailable",
                    ex.Message);

                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load workflow orders."),
                    orders = new List<LaundryOrderDto>()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> UpdateStoreWorkflowOrderStatus(string orderNo, string status)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (!CanManageStoreOrderWorkflow())
            {
                return Json(new { success = false, message = "Only admin and store users can update workflow status." });
            }

            if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(status))
            {
                return Json(new { success = false, message = "Order number and target status are required." });
            }

            try
            {
                var normalizedStatus = NormalizeWorkflowStatus(status);
                if (string.Equals(normalizedStatus, "Delivered", StringComparison.OrdinalIgnoreCase))
                {
                    var orderLookup = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "status", string.Empty },
                        { "searchText", orderNo.Trim() }
                    }, Method.GET);

                    var orderForDelivery = (orderLookup?.Orders ?? new List<LaundryOrderDto>())
                        .FirstOrDefault(x => x != null && string.Equals(x.OrderNo, orderNo.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (orderForDelivery != null)
                    {
                        var pendingAmount = orderForDelivery.PendingAmount > 0 ? orderForDelivery.PendingAmount : orderForDelivery.NetPayable;
                        if (pendingAmount > 0)
                        {
                            return Json(new
                            {
                                success = false,
                                message = $"Pending amount {pendingAmount:0.00} must be settled before moving order to Delivered."
                            });
                        }
                    }
                }

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/UpdateLaundryOrderStatus", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "orderNo", orderNo.Trim() },
                    { "status", status.Trim() }
                }, Method.POST);

                var isSuccess = response != null && response.StatusCode == "200";
                var message = response?.Message ?? "Unable to update workflow status.";

                if (isSuccess)
                {
                    var resolvedOrderNo = orderNo.Trim();
                    var context = await TryGetOrderCompletionContextAsync(resolvedOrderNo, allowFallback: true);

                    if (context != null)
                    {
                        await LogOrderActionAuditAsync(context, "WorkflowStatusUpdate", $"Order workflow moved to '{normalizedStatus}'.");

                        if (string.Equals(normalizedStatus, "Delivered", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetEmail = context.CustomerEmail;
                            if (string.IsNullOrWhiteSpace(targetEmail) && !string.IsNullOrWhiteSpace(context.CustomerName))
                            {
                                var customerProfile = await ResolveCustomerByNameAsync(context.CustomerName);
                                targetEmail = customerProfile?.Email;
                                context.CustomerEmail = targetEmail;
                            }

                            if (!string.IsNullOrWhiteSpace(targetEmail))
                            {
                                var emailResult = await SendOrderBillEmailInternalAsync(context, targetEmail);
                                if (emailResult.Success)
                                {
                                    TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(context);
                                    await LogOrderActionAuditAsync(context, "EmailBillAfterPaymentConfirmation", "Bill emailed after payment confirmation.", targetEmail);
                                    message = $"{message} Bill emailed to customer.";
                                }
                                else
                                {
                                    await LogOrderActionAuditAsync(context, "EmailBillAfterPaymentConfirmationFailed", emailResult.Message, targetEmail);
                                    message = $"{message} Bill email failed: {emailResult.Message}";
                                }
                            }
                            else
                            {
                                await LogOrderActionAuditAsync(context, "EmailBillAfterPaymentConfirmationSkipped", "Payment confirmed but customer email is missing.");
                                message = $"{message} Customer email missing; bill email skipped.";
                            }
                        }
                    }
                    else
                    {
                        await LogOrderActionAuditAsync(tenantName, storeCode, resolvedOrderNo, resolvedOrderNo, "WorkflowStatusUpdate", $"Order workflow moved to '{normalizedStatus}'.");
                    }
                }

                return Json(new
                {
                    success = isSuccess,
                    message
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update workflow status.")
                });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> SettleOrderPendingPayment(string orderNo, decimal paidAmount, string paymentMode = null, string notes = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(orderNo))
            {
                return Json(new { success = false, message = "Order number is required." });
            }

            if (paidAmount <= 0)
            {
                return Json(new { success = false, message = "Paid amount should be greater than zero." });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<LaundryOrderDto>("/SettleLaundryOrderPayment", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "orderNo", orderNo.Trim() },
                    { "paidAmount", paidAmount.ToString("0.00", CultureInfo.InvariantCulture) },
                    { "paymentMode", paymentMode?.Trim() ?? string.Empty },
                    { "notes", notes?.Trim() ?? string.Empty }
                }, Method.POST);

                if (response == null || string.IsNullOrWhiteSpace(response.OrderNo))
                {
                    return Json(new { success = false, message = "Unable to settle pending payment." });
                }

                var context = await TryGetOrderCompletionContextAsync(response.OrderNo, allowFallback: true);
                if (context != null)
                {
                    context.PaidNow = response.PaidNow;
                    context.PendingAmount = response.PendingAmount;
                    context.NetPayable = response.NetPayable;
                    context.PaymentMode = response.PaymentMode;
                    TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(context);
                }

                await LogOrderActionAuditAsync(tenantName, storeCode, response.OrderNo, response.InvoiceNo, "SettlePendingPayment", $"Settled amount {paidAmount:0.00}. Pending: {response.PendingAmount:0.00}");

                return Json(new
                {
                    success = true,
                    message = response.PendingAmount > 0
                        ? $"Payment updated. Pending amount: {response.PendingAmount:0.00}."
                        : "Payment settled successfully.",
                    order = response
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to settle pending payment.")
                });
            }
        }

        private static string NormalizeWorkflowStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
            return status.Trim();
            }

            if (string.Equals(status, "In Process", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Processing", StringComparison.OrdinalIgnoreCase))
            {
                return "Processing";
            }

            if (string.Equals(status, "Ready for Delivery", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                return "Ready";
            }

            if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase))
            {
                return "Delivered";
            }

            return "Received";
        }

        private async Task<CustomerIOResponse> ResolveCustomerByNameAsync(string customerName)
        {
            var normalizedName = customerName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName) || !TryGetCustomerScope(out _, out _))
            {
                return null;
            }

            var customer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", normalizedName)), Method.GET);

            if (customer != null && customer.StatusCode == "200" && !string.IsNullOrWhiteSpace(customer.CustCode))
            {
                return customer;
            }

            var searchResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SearchCustomers", BuildScopedParameters(("searchText", normalizedName)), Method.GET);

            var matches = searchResponse?.CustomerNames ?? new List<string>();
            if (!matches.Any())
            {
                return customer;
            }

            var bestMatch = matches.FirstOrDefault(x => string.Equals(x?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
                            ?? matches.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(bestMatch))
            {
                return customer;
            }

            return await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", bestMatch.Trim())), Method.GET);
        }

        [HttpPost]
        public async Task<JsonResult> SaveLaundryItemPrice(LaundryItemPriceDto model)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can modify store prices." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            if (string.IsNullOrWhiteSpace(model.ServiceType)
                || string.IsNullOrWhiteSpace(model.Category)
                || string.IsNullOrWhiteSpace(model.ItemName))
            {
                return Json(new { success = false, message = "Service, category, and item name are required." });
            }

            if (model.UnitPrice < 0)
            {
                return Json(new { success = false, message = "Item price cannot be negative." });
            }

            try
            {
                var request = new
                {
                    TenantName = model.TenantName,
                    StoreCode = model.StoreCode,
                    ServiceType = model.ServiceType,
                    Category = model.Category,
                    ItemName = model.ItemName,
                    UnitPrice = model.UnitPrice,
                    IsActive = true
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveLaundryItemPrice", request, Method.POST);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Laundry item price saved successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to save laundry item price.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> DeactivateLaundryItemPrice(string serviceType, string category, string itemName)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can modify store prices." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return Json(new { success = false, message = "Service, category, and item name are required." });
            }

            try
            {
                var request = new
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = serviceType.Trim(),
                    Category = category.Trim(),
                    ItemName = itemName.Trim()
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/DeactivateLaundryItemPrice", request, Method.POST);

                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Laundry item removed successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to remove laundry item.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ReactivateLaundryItemPrice(string serviceType, string category, string itemName)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can modify store prices." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return Json(new { success = false, message = "Service, category, and item name are required." });
            }

            try
            {
                var request = new
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = serviceType.Trim(),
                    Category = category.Trim(),
                    ItemName = itemName.Trim()
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/ReactivateLaundryItemPrice", request, Method.POST);

                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Laundry item restored successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to restore laundry item.")
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetLaundryItemPrices([FromQuery] bool includeInactive = false, [FromQuery] bool forImportExport = false)
        {
            if (forImportExport && !IsAdminUser())
            {
                return Json(new
                {
                    success = false,
                    message = "Only admin users can access price import/export data.",
                    items = new List<LaundryItemPriceDto>()
                });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again.",
                    items = new List<LaundryItemPriceDto>()
                });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<List<LaundryItemPriceDto>>("/GetLaundryItemPrices", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "includeInactive", includeInactive ? "true" : "false" }
                }, Method.GET);

                var items = response ?? new List<LaundryItemPriceDto>();
                var inactiveCount = items.Count(x => !x.IsActive);

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    tenantName,
                    storeCode,
                    includeInactive,
                    totalCount = items.Count,
                    inactiveCount,
                    activeCount = items.Count - inactiveCount,
                    items
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load laundry item prices."),
                    items = new List<LaundryItemPriceDto>()
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ImportDefaultLaundryItemPrices(bool overwriteExisting = false)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can import default item prices." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/ImportDefaultLaundryItemPrices",
                    new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "overwriteExisting", overwriteExisting ? "true" : "false" }
                    }, Method.POST);

                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Default item pricing import completed."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to import default laundry item prices.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> ImportLaundryItemPricesCsv(IFormFile csvFile, bool overwriteExisting = false)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can import prices." });
            }

            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (csvFile == null || csvFile.Length == 0)
            {
                return Json(new { success = false, message = "Please choose a CSV file to import." });
            }

            if (csvFile.Length > MaxCsvUploadBytes)
            {
                return Json(new { success = false, message = "CSV file is too large. Max allowed size is 2 MB." });
            }

            var fileName = csvFile.FileName?.Trim() ?? string.Empty;
            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Invalid file type. Please upload a .csv file." });
            }

            try
            {
                List<string> lines;

                using (var stream = csvFile.OpenReadStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
                {
                    var allText = await reader.ReadToEndAsync();
                    lines = allText
                        .Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Split('\n')
                        .ToList();
                }

                var headerLine = lines
                    .Select(x => x ?? string.Empty)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (string.IsNullOrWhiteSpace(headerLine))
                {
                    return Json(new { success = false, message = "Selected CSV file is empty." });
                }

                var normalizedHeader = NormalizeCsvHeader(headerLine);

                const string requiredHeader = "servicetype,category,itemname,unitprice";
                if (!string.Equals(normalizedHeader, requiredHeader, StringComparison.Ordinal))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invalid CSV header. Required: ServiceType,Category,ItemName,UnitPrice"
                    });
                }

                var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!overwriteExisting)
                {
                    var existingItems = await _clientApi.SendRequestAsync<List<LaundryItemPriceDto>>("/GetLaundryItemPrices", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "includeInactive", "true" }
                    }, Method.GET) ?? new List<LaundryItemPriceDto>();

                    foreach (var existing in existingItems ?? new List<LaundryItemPriceDto>())
                    {
                        existingKeys.Add(BuildLaundryItemKey(existing.ServiceType, existing.Category, existing.ItemName));
                    }
                }

                var importedCount = 0;
                var skippedCount = 0;
                var failedCount = 0;
                var failedLineNumbers = new List<int>();
                var skippedLineNumbers = new List<int>();
                var rowIssues = new List<object>();
                var uploadedFileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rowNumber = 0;

                foreach (var line in lines)
                {
                    rowNumber++;

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var columns = ParseCsvLine(line);
                    if (columns.Count < 4)
                    {
                        failedCount++;
                        failedLineNumbers.Add(rowNumber);
                        continue;
                    }

                    var serviceType = columns[0]?.Trim();
                    var category = columns[1]?.Trim();
                    var itemName = columns[2]?.Trim();
                    var priceText = columns[3]?.Trim();

                    if (string.Equals(NormalizeCsvHeader(line), requiredHeader, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(serviceType)
                        || string.IsNullOrWhiteSpace(category)
                        || string.IsNullOrWhiteSpace(itemName)
                        || !decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var unitPrice)
                        || unitPrice < 0)
                    {
                        failedCount++;
                        failedLineNumbers.Add(rowNumber);
                        rowIssues.Add(new
                        {
                            lineNumber = rowNumber,
                            status = "failed",
                            reason = "Missing required values or invalid UnitPrice (must be a non-negative number using dot decimal format)."
                        });
                        continue;
                    }

                    var key = BuildLaundryItemKey(serviceType, category, itemName);

                    if (!uploadedFileKeys.Add(key))
                    {
                        failedCount++;
                        failedLineNumbers.Add(rowNumber);
                        rowIssues.Add(new
                        {
                            lineNumber = rowNumber,
                            status = "failed",
                            reason = "Duplicate item row in uploaded file for ServiceType + Category + ItemName."
                        });
                        continue;
                    }

                    if (!overwriteExisting && existingKeys.Contains(key))
                    {
                        skippedCount++;
                        skippedLineNumbers.Add(rowNumber);
                        rowIssues.Add(new
                        {
                            lineNumber = rowNumber,
                            status = "skipped",
                            reason = "Item already exists in current store and overwrite is disabled."
                        });
                        continue;
                    }

                    var request = new LaundryItemPriceDto
                    {
                        TenantName = tenantName,
                        StoreCode = storeCode,
                        ServiceType = serviceType,
                        Category = category,
                        ItemName = itemName,
                        UnitPrice = unitPrice,
                        IsActive = true
                    };

                    var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveLaundryItemPrice", request, Method.POST);
                    if (saveResponse != null && saveResponse.StatusCode == "200")
                    {
                        importedCount++;
                        existingKeys.Add(key);
                    }
                    else
                    {
                        failedCount++;
                        failedLineNumbers.Add(rowNumber);
                        rowIssues.Add(new
                        {
                            lineNumber = rowNumber,
                            status = "failed",
                            reason = string.IsNullOrWhiteSpace(saveResponse?.Message)
                                ? "Save failed for this row."
                                : saveResponse.Message
                        });
                    }
                }

                return Json(new
                {
                    success = failedCount == 0,
                    message = $"CSV import completed. Imported: {importedCount}, Skipped: {skippedCount}, Failed: {failedCount}.",
                    importedCount,
                    skippedCount,
                    failedCount,
                    failedLineNumbers,
                    skippedLineNumbers,
                    rowIssues
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to import custom CSV laundry item prices.")
                });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetStoreMasters(bool includeInactive = false)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            try
            {
                var query = new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "includeInactive", includeInactive ? "true" : "false" }
                };

                var services = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", query, Method.GET)
                               ?? new List<StoreServiceMasterDto>();
                if (!services.Any())
                {
                    services = await TryGetWrappedStoreServiceMastersAsync(query);
                }

                var items = await GetScopedStoreItemMastersAsync(tenantName, storeCode, includeInactive);

                var defaultPrices = await _clientApi.SendRequestAsync<List<LaundryItemPriceDto>>("/GetLaundryItemPrices", query, Method.GET)
                                   ?? new List<LaundryItemPriceDto>();

                if (services.Any())
                {
                    static string NormalizeServiceMasterTypeAlias(string masterType)
                    {
                        if (string.IsNullOrWhiteSpace(masterType))
                        {
                            return masterType;
                        }

                        var trimmed = masterType.Trim();
                        if (string.Equals(trimmed, "Service", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(trimmed, "ServiceType", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Service";
                        }

                        if (string.Equals(trimmed, "Category", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(trimmed, "ServiceCategory", StringComparison.OrdinalIgnoreCase))
                        {
                            return "Category";
                        }

                        return trimmed;
                    }

                    var knownServiceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var knownCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var item in items)
                    {
                        var serviceName = item?.ServiceType?.Trim();
                        if (!string.IsNullOrWhiteSpace(serviceName))
                        {
                            knownServiceNames.Add(serviceName);
                        }

                        var categoryName = item?.Category?.Trim();
                        if (!string.IsNullOrWhiteSpace(categoryName))
                        {
                            knownCategoryNames.Add(categoryName);
                        }
                    }

                    foreach (var price in defaultPrices)
                    {
                        var serviceName = price?.ServiceType?.Trim();
                        if (!string.IsNullOrWhiteSpace(serviceName))
                        {
                            knownServiceNames.Add(serviceName);
                        }

                        var categoryName = price?.Category?.Trim();
                        if (!string.IsNullOrWhiteSpace(categoryName))
                        {
                            knownCategoryNames.Add(categoryName);
                        }
                    }

                    foreach (var service in services)
                    {
                        if (service == null || !string.IsNullOrWhiteSpace(service.MasterType))
                        {
                            continue;
                        }

                        var name = service.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        var isKnownService = knownServiceNames.Contains(name);
                        var isKnownCategory = knownCategoryNames.Contains(name);

                        if (isKnownService && !isKnownCategory)
                        {
                            service.MasterType = "Service";
                        }
                        else if (isKnownCategory && !isKnownService)
                        {
                            service.MasterType = "Category";
                        }
                    }

                    if (services.Any(x => x != null && string.IsNullOrWhiteSpace(x.MasterType)))
                    {
                        var supplementalServices = await TryGetWrappedStoreServiceMastersAsync(query);
                        if (supplementalServices.Any())
                        {
                            static string NormalizeNameKey(string value)
                            {
                                return string.IsNullOrWhiteSpace(value)
                                    ? null
                                    : value.Trim().ToLowerInvariant();
                            }

                            static string BuildTypedIdentity(StoreServiceMasterDto row)
                            {
                                if (row == null
                                    || string.IsNullOrWhiteSpace(row.MasterType)
                                    || string.IsNullOrWhiteSpace(row.Name))
                                {
                                    return null;
                                }

                                return BuildStoreMasterKey(row.MasterType, row.Name);
                            }

                            static void MergeService(StoreServiceMasterDto target, StoreServiceMasterDto source)
                            {
                                if (target == null || source == null)
                                {
                                    return;
                                }

                                if (target.Id <= 0 && source.Id > 0)
                                {
                                    target.Id = source.Id;
                                }

                                target.TenantName ??= source.TenantName;
                                target.StoreCode ??= source.StoreCode;
                                target.Name ??= source.Name;

                                if (string.IsNullOrWhiteSpace(target.MasterType) && !string.IsNullOrWhiteSpace(source.MasterType))
                                {
                                    target.MasterType = source.MasterType;
                                }

                                if (string.IsNullOrWhiteSpace(target.Description) && !string.IsNullOrWhiteSpace(source.Description))
                                {
                                    target.Description = source.Description;
                                }

                                if (target.IsActive != source.IsActive)
                                {
                                    target.IsActive = false;
                                }
                            }

                            var mergedServices = new List<StoreServiceMasterDto>();
                            var byId = new Dictionary<int, StoreServiceMasterDto>();
                            var byTypedIdentity = new Dictionary<string, StoreServiceMasterDto>(StringComparer.OrdinalIgnoreCase);
                            var byName = new Dictionary<string, List<StoreServiceMasterDto>>(StringComparer.OrdinalIgnoreCase);

                            StoreServiceMasterDto FindMatch(StoreServiceMasterDto row)
                            {
                                if (row == null)
                                {
                                    return null;
                                }

                                if (row.Id > 0 && byId.TryGetValue(row.Id, out var byIdMatch))
                                {
                                    return byIdMatch;
                                }

                                var typedIdentity = BuildTypedIdentity(row);
                                if (!string.IsNullOrWhiteSpace(typedIdentity)
                                    && byTypedIdentity.TryGetValue(typedIdentity, out var byTypedMatch))
                                {
                                    return byTypedMatch;
                                }

                                if (string.IsNullOrWhiteSpace(row.MasterType))
                                {
                                    var nameKey = NormalizeNameKey(row.Name);
                                    if (!string.IsNullOrWhiteSpace(nameKey)
                                        && byName.TryGetValue(nameKey, out var candidates)
                                        && candidates.Count == 1)
                                    {
                                        return candidates[0];
                                    }
                                }

                                return null;
                            }

                            void Register(StoreServiceMasterDto row)
                            {
                                if (row == null)
                                {
                                    return;
                                }

                                if (row.Id > 0)
                                {
                                    byId[row.Id] = row;
                                }

                                var typedIdentity = BuildTypedIdentity(row);
                                if (!string.IsNullOrWhiteSpace(typedIdentity))
                                {
                                    byTypedIdentity[typedIdentity] = row;
                                }

                                var nameKey = NormalizeNameKey(row.Name);
                                if (!string.IsNullOrWhiteSpace(nameKey))
                                {
                                    if (!byName.TryGetValue(nameKey, out var list))
                                    {
                                        list = new List<StoreServiceMasterDto>();
                                        byName[nameKey] = list;
                                    }

                                    if (!list.Contains(row))
                                    {
                                        list.Add(row);
                                    }
                                }
                            }

                            void AddOrMerge(StoreServiceMasterDto row)
                            {
                                if (row == null || string.IsNullOrWhiteSpace(row.Name))
                                {
                                    return;
                                }

                                var existing = FindMatch(row);
                                if (existing == null)
                                {
                                    mergedServices.Add(row);
                                    Register(row);
                                    return;
                                }

                                MergeService(existing, row);
                                Register(existing);
                            }

                            foreach (var row in services)
                            {
                                AddOrMerge(row);
                            }

                            foreach (var row in supplementalServices)
                            {
                                AddOrMerge(row);
                            }

                            services = mergedServices;
                        }
                    }

                    foreach (var service in services)
                    {
                        if (service == null || string.IsNullOrWhiteSpace(service.MasterType))
                        {
                            continue;
                        }

                        service.MasterType = NormalizeServiceMasterTypeAlias(service.MasterType);
                    }
                }

                if (defaultPrices.Any())
                {
                    var now = DateTime.UtcNow;

                    var allowedServiceNames = new HashSet<string>(
                        services
                            .Where(x => x != null
                                && (includeInactive || x.IsActive)
                                && string.Equals(x.MasterType, "Service", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(x.Name))
                            .Select(x => x.Name.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    var allowedCategoryNames = new HashSet<string>(
                        services
                            .Where(x => x != null
                                && (includeInactive || x.IsActive)
                                && string.Equals(x.MasterType, "Category", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(x.Name))
                            .Select(x => x.Name.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    var defaultItems = defaultPrices
                        .Where(x => !string.IsNullOrWhiteSpace(x.ServiceType)
                                    && !string.IsNullOrWhiteSpace(x.Category)
                                    && !string.IsNullOrWhiteSpace(x.ItemName))
                        .GroupBy(x => new
                        {
                            ServiceType = x.ServiceType.Trim().ToLowerInvariant(),
                            Category = x.Category.Trim().ToLowerInvariant(),
                            ItemName = x.ItemName.Trim().ToLowerInvariant()
                        })
                        .Select(g =>
                        {
                            var row = g.First();
                            return new StoreItemMasterDto
                            {
                                TenantName = tenantName,
                                StoreCode = storeCode,
                                ServiceType = row.ServiceType?.Trim(),
                                Category = row.Category?.Trim(),
                                ItemName = row.ItemName?.Trim(),
                                IsActive = g.Any(x => x.IsActive),
                                CreatedDate = now,
                                ModifiedDate = now
                            };
                        })
                        .ToList();

                    var mergedServices = services
                        .Where(x => x != null
                            && (includeInactive || x.IsActive)
                            && !string.IsNullOrWhiteSpace(x.Name)
                            && (string.Equals(x.MasterType, "Service", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(x.MasterType, "Category", StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    var mergedItemsByKey = new Dictionary<string, StoreItemMasterDto>(StringComparer.OrdinalIgnoreCase);

                    void MergeItem(StoreItemMasterDto source)
                    {
                        if (source == null)
                        {
                            return;
                        }

                        var normalizedService = source.ServiceType?.Trim();
                        var normalizedCategory = source.Category?.Trim();
                        var normalizedItemName = source.ItemName?.Trim();

                        if (string.IsNullOrWhiteSpace(normalizedService)
                            || !allowedServiceNames.Contains(normalizedService)
                            || string.IsNullOrWhiteSpace(normalizedCategory)
                            || !allowedCategoryNames.Contains(normalizedCategory)
                            || string.IsNullOrWhiteSpace(normalizedItemName)
                            || (!includeInactive && !source.IsActive))
                        {
                            return;
                        }

                        var key = BuildLaundryItemKey(normalizedService, normalizedCategory, normalizedItemName);
                        if (string.IsNullOrWhiteSpace(key) || key == "||")
                        {
                            return;
                        }

                        source.ServiceType = normalizedService;
                        source.Category = normalizedCategory;
                        source.ItemName = normalizedItemName;

                        if (!mergedItemsByKey.TryGetValue(key, out var existing))
                        {
                            mergedItemsByKey[key] = source;
                            return;
                        }

                        if (existing.Id <= 0 && source.Id > 0)
                        {
                            existing.Id = source.Id;
                            existing.IsActive = source.IsActive;
                        }

                        if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(source.Description))
                        {
                            existing.Description = source.Description;
                        }

                        // Persisted item-master records (Id > 0) are authoritative for status.
                        // Derived/default rows (Id == 0) must never reactivate an archived persisted row.
                        if (source.Id > 0)
                        {
                            existing.IsActive = source.IsActive;
                        }
                        else if (existing.Id <= 0 && !existing.IsActive && source.IsActive)
                        {
                            existing.IsActive = true;
                        }
                    }

                    foreach (var item in items)
                    {
                        MergeItem(item);
                    }

                    foreach (var item in defaultItems)
                    {
                        MergeItem(item);
                    }

                    var mergedItems = mergedItemsByKey.Values.ToList();

                    services = mergedServices;
                    items = mergedItems;
                }

                return Json(new
                {
                    success = true,
                    tenantName,
                    storeCode,
                    includeInactive,
                    serviceTypes = services.Where(x => string.Equals(x.MasterType, "Service", StringComparison.OrdinalIgnoreCase)),
                    categories = services.Where(x => string.Equals(x.MasterType, "Category", StringComparison.OrdinalIgnoreCase)),
                    items
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to load store master data.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SaveStoreServiceMaster(string masterType, string name, string description)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Json(new { success = false, message = "Name is required." });
            }

            try
            {
                var request = new StoreServiceMasterDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    MasterType = masterType,
                    Name = name.Trim(),
                    Description = description?.Trim(),
                    IsActive = true
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreServiceMaster", request, Method.POST);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Master entry saved successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to save master entry.")
                });
            }
        }

        /// <summary>
        /// Imports service types and categories for the logged-in store from a CSV file
        /// using the MasterType,Name,Description layout. Names that already exist in this
        /// store's master are skipped so existing entries are never overwritten.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> ImportStoreServiceMastersCsv(IFormFile csvFile)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (csvFile == null || csvFile.Length == 0)
            {
                return Json(new { success = false, message = "Please choose a CSV file to import." });
            }

            try
            {
                var existingMasters = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "includeInactive", "true" }
                }, Method.GET) ?? new List<StoreServiceMasterDto>();

                var existingKeys = new HashSet<string>(
                    existingMasters.Select(x => BuildStoreMasterKey(x.MasterType, x.Name)),
                    StringComparer.OrdinalIgnoreCase);

                var importedCount = 0;
                var skippedCount = 0;
                var failedCount = 0;
                var failedLineNumbers = new List<int>();
                var skippedLineNumbers = new List<int>();
                var rowNumber = 0;

                using (var stream = csvFile.OpenReadStream())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        rowNumber++;

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var columns = ParseCsvLine(line);
                        if (columns.Count < 2)
                        {
                            failedCount++;
                            failedLineNumbers.Add(rowNumber);
                            continue;
                        }

                        var masterType = columns[0]?.Trim();
                        var name = columns[1]?.Trim();
                        var description = columns.Count > 2 ? columns[2]?.Trim() : string.Empty;

                        if (rowNumber == 1
                            && string.Equals(masterType, "MasterType", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var isService = string.Equals(masterType, "Service", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(masterType, "ServiceType", StringComparison.OrdinalIgnoreCase);
                        var isCategory = string.Equals(masterType, "Category", StringComparison.OrdinalIgnoreCase);

                        if (string.IsNullOrWhiteSpace(name) || (!isService && !isCategory))
                        {
                            failedCount++;
                            failedLineNumbers.Add(rowNumber);
                            continue;
                        }

                        var normalizedType = isService ? "Service" : "Category";
                        var key = BuildStoreMasterKey(normalizedType, name);

                        if (existingKeys.Contains(key))
                        {
                            skippedCount++;
                            skippedLineNumbers.Add(rowNumber);
                            continue;
                        }

                        var request = new StoreServiceMasterDto
                        {
                            TenantName = tenantName,
                            StoreCode = storeCode,
                            MasterType = normalizedType,
                            Name = name,
                            Description = description,
                            IsActive = true
                        };

                        var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreServiceMaster", request, Method.POST);
                        if (saveResponse != null && saveResponse.StatusCode == "200")
                        {
                            importedCount++;
                            existingKeys.Add(key);
                        }
                        else
                        {
                            failedCount++;
                            failedLineNumbers.Add(rowNumber);
                        }
                    }
                }

                return Json(new
                {
                    success = failedCount == 0,
                    message = $"Import completed. Added: {importedCount}, Skipped (already present): {skippedCount}, Failed: {failedCount}.",
                    importedCount,
                    skippedCount,
                    failedCount,
                    failedLineNumbers,
                    skippedLineNumbers
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to import service types and categories.")
                });
            }
        }

        private static string BuildStoreMasterKey(string masterType, string name)
        {
            return $"{masterType?.Trim().ToLowerInvariant()}|{name?.Trim().ToLowerInvariant()}";
        }

        [HttpPost]
        public async Task<JsonResult> SetStoreServiceMasterStatus(string masterType, string name, bool isActive, int? id = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Json(new { success = false, message = "Name is required." });
            }

            try
            {
                var normalizedMasterType = masterType?.Trim();
                var normalizedName = name.Trim();
                var resolvedId = id.GetValueOrDefault();

                if (isActive)
                {
                    var existingMasters = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "includeInactive", "true" }
                    }, Method.GET) ?? new List<StoreServiceMasterDto>();

                    var hasInactiveTarget = existingMasters.Any(x =>
                        string.Equals(x.MasterType, normalizedMasterType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                        && !x.IsActive);

                    var hasActiveDuplicate = existingMasters.Any(x =>
                        string.Equals(x.MasterType, normalizedMasterType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                        && x.IsActive);

                    if (hasInactiveTarget && hasActiveDuplicate)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "An active entry with the same name already exists for this store. Restore is blocked to prevent duplicates."
                        });
                    }

                    if (resolvedId <= 0)
                    {
                        resolvedId = existingMasters
                            .Where(x => string.Equals(x.MasterType, normalizedMasterType, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                                && !x.IsActive)
                            .Select(x => x.Id)
                            .FirstOrDefault(x => x > 0);
                    }
                }
                else if (resolvedId <= 0)
                {
                    var existingMasters = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "includeInactive", "true" }
                    }, Method.GET) ?? new List<StoreServiceMasterDto>();

                    resolvedId = existingMasters
                        .Where(x => string.Equals(x.MasterType, normalizedMasterType, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                            && x.IsActive)
                        .Select(x => x.Id)
                        .FirstOrDefault(x => x > 0);
                }

                var request = new StoreServiceMasterDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    Id = resolvedId,
                    MasterType = normalizedMasterType,
                    Name = normalizedName,
                    IsActive = isActive
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SetStoreServiceMasterStatus", request, Method.POST);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Master entry updated successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update master entry.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SaveStoreItemMaster(string serviceType, string category, string itemName, string description)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return Json(new { success = false, message = "Service, category, and item name are required." });
            }

            try
            {
                var request = new StoreItemMasterDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = serviceType.Trim(),
                    Category = category.Trim(),
                    ItemName = itemName.Trim(),
                    Description = description?.Trim(),
                    IsActive = true
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreItemMaster", request, Method.POST);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Item master saved successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to save item master entry.")
                });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SetStoreItemMasterStatus(string serviceType, string category, string itemName, bool isActive, int? id = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please log in again." });
            }

            if (string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return Json(new { success = false, message = "Service, category, and item name are required." });
            }

            try
            {
                var normalizedServiceType = serviceType.Trim();
                var normalizedCategory = category.Trim();
                var normalizedItemName = itemName.Trim();
                var resolvedId = id.GetValueOrDefault();

                List<StoreItemMasterDto> existingItems = null;
                if (isActive || resolvedId <= 0)
                {
                    existingItems = await GetScopedStoreItemMastersAsync(tenantName, storeCode, includeInactive: true);
                }

                if (isActive)
                {
                    var existingServices = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "includeInactive", "true" }
                    }, Method.GET) ?? new List<StoreServiceMasterDto>();

                    var hasActiveService = existingServices.Any(x =>
                        string.Equals(x.MasterType, "Service", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Name, normalizedServiceType, StringComparison.OrdinalIgnoreCase)
                        && x.IsActive);

                    var hasActiveCategory = existingServices.Any(x =>
                        string.Equals(x.MasterType, "Category", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Name, normalizedCategory, StringComparison.OrdinalIgnoreCase)
                        && x.IsActive);

                    if (!hasActiveService || !hasActiveCategory)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Cannot restore item while its mapped service type or category is inactive. Restore those masters first."
                        });
                    }

                    var inactiveMatches = (existingItems ?? new List<StoreItemMasterDto>()).Where(x =>
                        string.Equals((x.ServiceType ?? string.Empty).Trim(), normalizedServiceType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals((x.Category ?? string.Empty).Trim(), normalizedCategory, StringComparison.OrdinalIgnoreCase)
                        && string.Equals((x.ItemName ?? string.Empty).Trim(), normalizedItemName, StringComparison.OrdinalIgnoreCase)
                        && !x.IsActive).ToList();

                    var hasActiveDuplicate = (existingItems ?? new List<StoreItemMasterDto>()).Any(x =>
                        string.Equals((x.ServiceType ?? string.Empty).Trim(), normalizedServiceType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals((x.Category ?? string.Empty).Trim(), normalizedCategory, StringComparison.OrdinalIgnoreCase)
                        && string.Equals((x.ItemName ?? string.Empty).Trim(), normalizedItemName, StringComparison.OrdinalIgnoreCase)
                        && x.IsActive);

                    if (inactiveMatches.Any() && hasActiveDuplicate)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "An active item with the same service/category/name already exists. Restore is blocked to prevent duplicates."
                        });
                    }

                    if (resolvedId <= 0)
                    {
                        resolvedId = inactiveMatches.Select(x => x.Id).FirstOrDefault(x => x > 0);
                    }
                }
                else if (resolvedId <= 0)
                {
                    resolvedId = (existingItems ?? new List<StoreItemMasterDto>())
                        .Where(x => string.Equals((x.ServiceType ?? string.Empty).Trim(), normalizedServiceType, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((x.Category ?? string.Empty).Trim(), normalizedCategory, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((x.ItemName ?? string.Empty).Trim(), normalizedItemName, StringComparison.OrdinalIgnoreCase)
                            && x.IsActive)
                        .Select(x => x.Id)
                        .FirstOrDefault(x => x > 0);
                }

                var request = new StoreItemMasterDto
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    Id = resolvedId,
                    ServiceType = normalizedServiceType,
                    Category = normalizedCategory,
                    ItemName = normalizedItemName,
                    IsActive = isActive
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SetStoreItemMasterStatus", request, Method.POST);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Item master updated successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = BuildApiErrorMessage(ex, "Unable to update item master entry.")
                });
            }
        }

        
           [HttpGet]
            public async Task<JsonResult> GetSelectedCustomerOrders()
            {
            if (!TryGetCustomerScope(out var tenantName, out var storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please log in again.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            var custCode = HttpContext.Session.GetString(
                SelectedCustomerCodeSessionKey);

            if (string.IsNullOrWhiteSpace(custCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Please search for a customer first.",
                    orders = new List<LaundryOrderDto>()
                });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>(
                    "/GetLaundryOrders",
                    new Dictionary<string, string>
                    {
                { "tenantName", tenantName },
                { "storeCode", storeCode },
                { "custCode", custCode }
                    },
                    Method.GET);

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    orders = response?.Orders ?? new List<LaundryOrderDto>()
                });
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    message = "Unable to load customer orders. Please try again.",
                    orders = new List<LaundryOrderDto>()
                });
            }
        }


        private static string BuildLaundryItemKey(string serviceType, string category, string itemName)
        {
            return $"{serviceType?.Trim().ToLowerInvariant()}|{category?.Trim().ToLowerInvariant()}|{itemName?.Trim().ToLowerInvariant()}";
        }

        private async Task<List<StoreItemMasterDto>> GetScopedStoreItemMastersAsync(string tenantName, string storeCode, bool includeInactive)
        {
            var query = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode },
                { "includeInactive", includeInactive ? "true" : "false" }
            };

            var directItems = await _clientApi.SendRequestAsync<List<StoreItemMasterDto>>("/GetStoreItemMasters", query, Method.GET)
                ?? new List<StoreItemMasterDto>();
            var wrappedItems = await TryGetWrappedStoreItemMastersAsync(query);

            static string BuildCompositeKey(StoreItemMasterDto item)
            {
                if (item == null)
                {
                    return null;
                }

                var key = BuildLaundryItemKey(item.ServiceType, item.Category, item.ItemName);
                return string.IsNullOrWhiteSpace(key) || key == "||"
                    ? null
                    : key;
            }

            static void MergeInto(StoreItemMasterDto target, StoreItemMasterDto source)
            {
                if (target == null || source == null)
                {
                    return;
                }

                if (target.Id <= 0 && source.Id > 0)
                {
                    target.Id = source.Id;
                }

                target.TenantName ??= source.TenantName;
                target.StoreCode ??= source.StoreCode;
                target.ServiceType ??= source.ServiceType;
                target.Category ??= source.Category;
                target.ItemName ??= source.ItemName;

                if (string.IsNullOrWhiteSpace(target.Description) && !string.IsNullOrWhiteSpace(source.Description))
                {
                    target.Description = source.Description;
                }

                if (target.IsActive != source.IsActive)
                {
                    target.IsActive = false;
                }
            }

            var byId = new Dictionary<int, StoreItemMasterDto>();
            var byComposite = new Dictionary<string, StoreItemMasterDto>(StringComparer.OrdinalIgnoreCase);

            void AddOrMerge(StoreItemMasterDto item)
            {
                if (item == null)
                {
                    return;
                }

                var compositeKey = BuildCompositeKey(item);
                var hasId = item.Id > 0;

                if (hasId)
                {
                    if (byId.TryGetValue(item.Id, out var existingById))
                    {
                        MergeInto(existingById, item);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(compositeKey)
                        && byComposite.TryGetValue(compositeKey, out var existingByComposite))
                    {
                        MergeInto(existingByComposite, item);
                        byId[item.Id] = existingByComposite;
                        return;
                    }

                    byId[item.Id] = item;
                    if (!string.IsNullOrWhiteSpace(compositeKey))
                    {
                        byComposite[compositeKey] = item;
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(compositeKey))
                {
                    return;
                }

                if (byComposite.TryGetValue(compositeKey, out var existingWithoutId))
                {
                    MergeInto(existingWithoutId, item);
                    return;
                }

                byComposite[compositeKey] = item;
            }

            foreach (var item in directItems)
            {
                AddOrMerge(item);
            }

            foreach (var item in wrappedItems)
            {
                AddOrMerge(item);
            }

            var merged = byId.Values.ToList();
            foreach (var item in byComposite.Values)
            {
                if (item.Id <= 0 || !byId.ContainsKey(item.Id))
                {
                    merged.Add(item);
                }
            }

            return merged;
        }

        private static List<StoreServiceMasterDto> ExtractStoreServiceMasters(JObject response)
        {
            if (response == null)
            {
                return new List<StoreServiceMasterDto>();
            }

            static IEnumerable<JObject> EnumerateContainers(JObject root)
            {
                if (root == null)
                {
                    yield break;
                }

                yield return root;

                var firstLevel = new[] { "result", "Result", "data", "Data", "resultSet", "ResultSet" }
                    .Select(name => root[name])
                    .OfType<JObject>();

                foreach (var container in firstLevel)
                {
                    yield return container;

                    var secondLevel = new[] { "result", "Result", "data", "Data", "resultSet", "ResultSet" }
                        .Select(name => container[name])
                        .OfType<JObject>();

                    foreach (var nested in secondLevel)
                    {
                        yield return nested;
                    }
                }
            }

            static JArray GetArrayFromContainer(JObject container, string camelName, string pascalName)
            {
                if (container == null)
                {
                    return null;
                }

                var token = container[camelName] ?? container[pascalName];
                return token as JArray ?? token?["$values"] as JArray;
            }

            static List<StoreServiceMasterDto> ParseArray(JArray source, string fallbackMasterType)
            {
                var parsed = source?.ToObject<List<StoreServiceMasterDto>>() ?? new List<StoreServiceMasterDto>();
                foreach (var row in parsed)
                {
                    if (row != null && string.IsNullOrWhiteSpace(row.MasterType))
                    {
                        row.MasterType = fallbackMasterType;
                    }
                }

                return parsed;
            }

            var combined = new List<StoreServiceMasterDto>();

            foreach (var container in EnumerateContainers(response))
            {
                combined.AddRange(ParseArray(GetArrayFromContainer(container, "serviceTypes", "ServiceTypes"), "Service"));
                combined.AddRange(ParseArray(GetArrayFromContainer(container, "categories", "Categories"), "Category"));
            }

            if (!combined.Any())
            {
                var fallbackArray = response["$values"] as JArray;
                combined = ParseArray(fallbackArray, fallbackMasterType: null);
            }

            var byId = new Dictionary<int, StoreServiceMasterDto>();
            var byKey = new Dictionary<string, StoreServiceMasterDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in combined)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name))
                {
                    continue;
                }

                if (row.Id > 0)
                {
                    if (!byId.TryGetValue(row.Id, out var existingById))
                    {
                        byId[row.Id] = row;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(existingById.MasterType) && !string.IsNullOrWhiteSpace(row.MasterType))
                        {
                            existingById.MasterType = row.MasterType;
                        }

                        if (existingById.IsActive != row.IsActive)
                        {
                            existingById.IsActive = false;
                        }
                    }

                    var idKey = BuildStoreMasterKey(row.MasterType, row.Name);
                    if (!string.IsNullOrWhiteSpace(idKey))
                    {
                        byKey[idKey] = byId[row.Id];
                    }

                    continue;
                }

                var key = BuildStoreMasterKey(row.MasterType, row.Name);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!byKey.TryGetValue(key, out var existingByKey))
                {
                    byKey[key] = row;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(existingByKey.MasterType) && !string.IsNullOrWhiteSpace(row.MasterType))
                    {
                        existingByKey.MasterType = row.MasterType;
                    }

                    if (existingByKey.IsActive != row.IsActive)
                    {
                        existingByKey.IsActive = false;
                    }
                }
            }

            var merged = byId.Values.ToList();
            foreach (var row in byKey.Values)
            {
                if (row.Id <= 0 || !byId.ContainsKey(row.Id))
                {
                    merged.Add(row);
                }
            }

            return merged;
        }

        private static List<StoreItemMasterDto> ExtractStoreItemMasters(JObject response)
        {
            if (response == null)
            {
                return new List<StoreItemMasterDto>();
            }

            JToken token = response["items"]
                ?? response["Items"]
                ?? response["resultSet"]
                ?? response["ResultSet"]
                ?? response["result"]
                ?? response["Result"]
                ?? response["data"]
                ?? response["Data"];

            if (token is JObject obj)
            {
                token = obj["items"]
                    ?? obj["Items"]
                    ?? obj["resultSet"]
                    ?? obj["ResultSet"]
                    ?? obj["result"]
                    ?? obj["Result"]
                    ?? obj["data"]
                    ?? obj["Data"]
                    ?? obj["$values"];

                if (token is JObject nestedObject)
                {
                    token = nestedObject["items"]
                        ?? nestedObject["Items"]
                        ?? nestedObject["resultSet"]
                        ?? nestedObject["ResultSet"]
                        ?? nestedObject["result"]
                        ?? nestedObject["Result"]
                        ?? nestedObject["data"]
                        ?? nestedObject["Data"]
                        ?? nestedObject["$values"]
                        ?? nestedObject;
                }
            }

            var array = token as JArray
                ?? token?["$values"] as JArray
                ?? (token as JObject)?.Descendants().OfType<JArray>().FirstOrDefault()
                ?? (response as JObject)?.Descendants().OfType<JArray>().FirstOrDefault()
                ?? (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Undefined && token.Type != JTokenType.Object
                    ? JArray.FromObject(token)
                    : null);
            if (array == null)
            {
                return new List<StoreItemMasterDto>();
            }

            var items = new List<StoreItemMasterDto>();
            foreach (var itemToken in array)
            {
                var candidateToken = itemToken;
                if (itemToken is JObject itemObject)
                {
                    candidateToken = itemObject["item"]
                        ?? itemObject["Item"]
                        ?? itemObject["storeItemMaster"]
                        ?? itemObject["StoreItemMaster"]
                        ?? itemObject["result"]
                        ?? itemObject["Result"]
                        ?? itemObject["data"]
                        ?? itemObject["Data"]
                        ?? itemToken;
                }

                var dto = candidateToken?.ToObject<StoreItemMasterDto>() ?? new StoreItemMasterDto();

                var idSource = candidateToken as JObject ?? itemToken as JObject;
                if (dto.Id <= 0 && idSource != null)
                {
                    dto.Id = ResolveStoreItemMasterId(idSource);
                }

                if ((string.IsNullOrWhiteSpace(dto.ServiceType)
                    || string.IsNullOrWhiteSpace(dto.Category)
                    || string.IsNullOrWhiteSpace(dto.ItemName))
                    && itemToken is JObject fallbackObject)
                {
                    dto.ServiceType = dto.ServiceType ?? fallbackObject["ServiceType"]?.ToString() ?? fallbackObject["serviceType"]?.ToString();
                    dto.Category = dto.Category ?? fallbackObject["Category"]?.ToString() ?? fallbackObject["category"]?.ToString();
                    dto.ItemName = dto.ItemName ?? fallbackObject["ItemName"]?.ToString() ?? fallbackObject["itemName"]?.ToString();
                    dto.Description = dto.Description ?? fallbackObject["Description"]?.ToString() ?? fallbackObject["description"]?.ToString();
                }

                items.Add(dto);
            }

            return items.Where(x => !string.IsNullOrWhiteSpace(x.ServiceType)
                && !string.IsNullOrWhiteSpace(x.Category)
                && !string.IsNullOrWhiteSpace(x.ItemName)).ToList();
        }

        private async Task<List<StoreServiceMasterDto>> TryGetWrappedStoreServiceMastersAsync(Dictionary<string, string> query)
        {
            try
            {
                var wrappedServices = await _clientApi.SendRequestAsync<JObject>("/GetStoreServiceMasters", query, Method.GET);
                return ExtractStoreServiceMasters(wrappedServices);
            }
            catch (ApiUnavailableException ex) when (IsInvalidJsonObjectDiagnostic(ex))
            {
                return new List<StoreServiceMasterDto>();
            }
        }

        private async Task<List<StoreItemMasterDto>> TryGetWrappedStoreItemMastersAsync(Dictionary<string, string> query)
        {
            try
            {
                var wrappedItemsResponse = await _clientApi.SendRequestAsync<JObject>("/GetStoreItemMasters", query, Method.GET);
                return ExtractStoreItemMasters(wrappedItemsResponse);
            }
            catch (ApiUnavailableException ex) when (IsInvalidJsonObjectDiagnostic(ex))
            {
                return new List<StoreItemMasterDto>();
            }
        }

        private static bool IsInvalidJsonObjectDiagnostic(ApiUnavailableException ex)
        {
            return !string.IsNullOrWhiteSpace(ex?.Message)
                && ex.Message.Contains("BodyState=InvalidJsonObject", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveStoreItemMasterId(JObject itemObject)
        {
            if (itemObject == null)
            {
                return 0;
            }

            var idToken = itemObject["Id"]
                ?? itemObject["id"]
                ?? itemObject["StoreItemMasterId"]
                ?? itemObject["storeItemMasterId"]
                ?? itemObject["ItemMasterId"]
                ?? itemObject["itemMasterId"]
                ?? itemObject["ItemId"]
                ?? itemObject["itemId"];

            if (idToken == null)
            {
                return 0;
            }

            if (idToken.Type == JTokenType.Integer && idToken.Value<int>() > 0)
            {
                return idToken.Value<int>();
            }

            if (int.TryParse(idToken.ToString(), out var parsedId) && parsedId > 0)
            {
                return parsedId;
            }

            return 0;
        }

        private static string NormalizeCsvHeader(string headerLine)
        {
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return string.Empty;
            }

            var withoutBom = headerLine.Replace("\uFEFF", string.Empty);
            var withoutWhitespace = new string(withoutBom.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return withoutWhitespace.Trim().ToLowerInvariant();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null)
            {
                return result;
            }

            var current = string.Empty;
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = string.Empty;
                    continue;
                }

                current += ch;
            }

            result.Add(current);
            return result;
        }

        private static string BuildApiErrorMessage(Exception ex, string fallbackMessage, string conflictMessage = null)
        {
            var rawMessage = ex?.InnerException?.Message ?? ex?.Message;
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return fallbackMessage;
            }

            if (!string.IsNullOrWhiteSpace(conflictMessage)
                && rawMessage.Contains("status code Conflict", StringComparison.OrdinalIgnoreCase))
            {
                return conflictMessage;
            }

            try
            {
                if (rawMessage.StartsWith("{", StringComparison.Ordinal))
                {
                    var token = JToken.Parse(rawMessage);
                    var message = token["Message"]?.ToString()
                                  ?? token["message"]?.ToString()
                                  ?? token["ResultSet"]?["Message"]?.ToString()
                                  ?? token["resultSet"]?["message"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message;
                    }
                }
            }
            catch
            {
                // Fall through to the fallback message
            }

            return $"{fallbackMessage} {rawMessage}";
        }

        private static bool IsNotFoundApiResponse(Exception ex)
        {
            var rawMessage = ex?.InnerException?.Message ?? ex?.Message;
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return false;
            }

            if (rawMessage.Contains("status code NotFound", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("\"StatusCode\":\"404\"", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("\"statusCode\":\"404\"", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("\"StatusCode\":404", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("\"statusCode\":404", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                if (!rawMessage.StartsWith("{", StringComparison.Ordinal))
                {
                    return false;
                }

                var token = JToken.Parse(rawMessage);
                var statusCodeText = token["StatusCode"]?.ToString()
                                     ?? token["statusCode"]?.ToString()
                                     ?? token["ResultSet"]?["StatusCode"]?.ToString()
                                     ?? token["resultSet"]?["statusCode"]?.ToString();

                return string.Equals(statusCodeText, "404", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeSerializeForLogging(object payload)
        {
            if (payload == null)
            {
                return "null";
            }

            try
            {
                return JsonConvert.SerializeObject(payload);
            }
            catch (Exception ex)
            {
                return $"<serialization-failed:{ex.GetType().Name}:{ex.Message}>";
            }
        }

        private static (string StatusCode, string Message) TryExtractApiStatusAndMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody)
                || !responseBody.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return (null, null);
            }

            try
            {
                var token = JToken.Parse(responseBody);
                var statusCode = token["StatusCode"]?.ToString()
                                 ?? token["statusCode"]?.ToString()
                                 ?? token["ResultSet"]?["StatusCode"]?.ToString()
                                 ?? token["resultSet"]?["statusCode"]?.ToString();

                var message = token["Message"]?.ToString()
                              ?? token["message"]?.ToString()
                              ?? token["ResultSet"]?["Message"]?.ToString()
                              ?? token["resultSet"]?["message"]?.ToString();

                return (statusCode, message);
            }
            catch
            {
                return (null, null);
            }
        }

        private static bool IsApiCallSuccessful(string statusCode, bool hasValidPayload)
        {
            if (!string.IsNullOrWhiteSpace(statusCode))
            {
                return IsSuccessfulApiStatusCode(statusCode);
            }

            return hasValidPayload;
        }

        private static bool IsSuccessfulApiStatusCode(string statusCode)
        {
            return string.Equals(statusCode, "200", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusCode, "201", StringComparison.OrdinalIgnoreCase);
        }

        private static SqlException TryGetSqlException(Exception ex)
        {
            if (ex == null)
            {
                return null;
            }

            if (ex is SqlException direct)
            {
                return direct;
            }

            return ex.InnerException as SqlException;
        }
    }
}