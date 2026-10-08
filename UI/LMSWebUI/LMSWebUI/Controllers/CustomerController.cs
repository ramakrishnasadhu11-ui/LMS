using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Microsoft.Extensions.Configuration;

namespace LMSWebUI.Controllers
{
    public class CustomerController : Controller
    {
        private readonly ICustomerApiClient _clientApi;
        private readonly ILoginApiClient _loginApi;
        private readonly IStoreConfigurationService _storeConfigurationService;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private const string CustomerPreferencesSessionKey = "CustomerPreferences";
        private const string SelectedCustomerNameSessionKey = "SelectedCustomerName";
        private const string SelectedCustomerCodeSessionKey = "SelectedCustomerCode";
        private const string AllowedStoresSessionKey = "TenantAllowedStores";
        private const string OrderPiecesMapSessionKey = "OrderPiecesMap";
        private const string CustomerNotInStoreMessage = "Customer not found in the current store.";

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
<<<<<<< Updated upstream
             responseMessage = await clientAPI.SendRequestAsync<CustomerIOResponse>("/GetCustomers", paramsGetAllStoresByClient, RestSharp.Method.GET);
                    if (responseMessage!=null && responseMessage.StatusCode=="200")
                    {
                      return Json(responseMessage.CustomerNames);
                     }
               return Json(responseMessage.CustomerNames);
         }
=======
>>>>>>> Stashed changes

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
                validationMessage = "Session expired. Please login again.";
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
<<<<<<< Updated upstream
                    responseMessage = await clientAPI.SendRequestAsync<CustomerIOResponse>("/InsertCustomer", CustomerInfoDto, RestSharp.Method.POST);
                    if (responseMessage!=null && responseMessage.StatusCode=="200")
=======
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
>>>>>>> Stashed changes
                    {
                        { "tenantEmail", scopeTenantEmail },
                        { "storeCode", scopeStoreCode }
                    }, Method.Get);

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
                    var response = await _loginApi.SendRequestAsync<LoginIoResponse>("/GetTenantStoreDetails", parms, Method.Get);
                    if (response?.Storecodes != null)
                    {
                        storeCodes.AddRange(response.Storecodes);
                    }
                }
                catch
                {
                    try
                    {
                        var fallbackResponse = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.Get);
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
            ICustomerApiClient clientAPI,
            ILoginApiClient loginApi,
            IStoreConfigurationService storeConfigurationService,
            IConfiguration configuration,
            ApplicationDbContext dbContext)
        {
            _clientApi = clientAPI;
            _loginApi = loginApi;
            _storeConfigurationService = storeConfigurationService;
            _configuration = configuration;
            _dbContext = dbContext;
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
                    var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", parameters, Method.Get);
                    if (responseMessage != null && responseMessage.StatusCode == "200")
                    {
                        customerInfoDto.CustomerName = responseMessage.CustomerName;
                        customerInfoDto.Address = responseMessage.Address;
                        // MembershipId removed system-wide
                        customerInfoDto.BarCode = responseMessage.BarCode;
                        customerInfoDto.PhoneNumber = responseMessage.PhoneNumber;
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
                var prefs = await _clientApi.SendRequestAsync<CustomerPreferencesDto>("/GetCustomerPreferences", prefParams, Method.Get);
                ViewData["CustomerPreferences"] = JsonConvert.SerializeObject(prefs ?? BuildDefaultCustomerPreferences(HttpContext.Session.GetString("TenantName"), HttpContext.Session.GetString("TenantStore")));
            }
            catch
            {
                ViewData["CustomerPreferences"] = JsonConvert.SerializeObject(BuildDefaultCustomerPreferences(HttpContext.Session.GetString("TenantName"), HttpContext.Session.GetString("TenantStore")));
            }

            return View(customerInfoDto);
        }

        [HttpGet]
        public IActionResult AddNewCustomer()
        {
            SetCreateModeState();
            return View("Index", new CustomerInfoDto
            {
                disableStatus = false
            });
        }

        [HttpGet]
        public IActionResult CustomerPreferences()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            CustomerPreferencesDto model;

            try
            {
                var response = _clientApi.SendRequestAsync<CustomerPreferencesDto>("/GetCustomerPreferences", parameters, Method.Get)
                    .GetAwaiter().GetResult();

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
                    DisplayMessage = "Only admin users can access Store Configuration."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            var model = await _storeConfigurationService.LoadAsync(tenantName, storeCode);

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.IsExistingRecord = model.IsExistingRecord || _storeConfigurationService.HasData(model);

            if (!string.IsNullOrWhiteSpace(model.LoadErrorMessage) && TempData["UserMessage"] == null)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-danger",
                    Title = "Fail!",
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
                    DisplayMessage = "Only admin users can access Store Configuration."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            model = _storeConfigurationService.Normalize(model);
            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            var existingConfig = await _storeConfigurationService.LoadAsync(tenantName, storeCode);
            model.IsExistingRecord = _storeConfigurationService.HasData(existingConfig);

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
                    Title = "Warning!",
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
                        Title = "Fail!",
                        DisplayMessage = string.IsNullOrWhiteSpace(saveMessage)
                            ? "Unable to save store configuration"
                            : saveMessage
                    });

                    return View(model);
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
                    Title = "Fail!",
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
                    DisplayMessage = "Only admin users can access Pricing Rules."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            PricingRulesViewModel model;
            var parameters = new Dictionary<string, string>
            {
                { "tenantName", tenantName },
                { "storeCode", storeCode }
            };

            try
            {
                var response = await _clientApi.SendRequestAsync<PricingRulesViewModel>("/GetPricingRules", parameters, Method.Get);
                model = response ?? BuildDefaultPricingRules(tenantName, storeCode);
            }
            catch
            {
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
                    DisplayMessage = "Only admin users can access Pricing Rules."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    Title = "Warning!",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SavePricingRules", model, Method.Post);
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
                    Title = "Fail!",
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    DisplayMessage = "Only admin users can access Tax & Invoice Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            var model = await LoadTaxInvoiceSettingsAsync(tenantName, storeCode);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TaxInvoiceSettings(TaxInvoiceSettingsViewModel model)
        {
            if (!IsAdminUser())
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Access Denied!",
                    DisplayMessage = "Only admin users can access Tax & Invoice Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;
            model.CompanyName = await GetCompanyNameAsync(tenantName, storeCode);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!ValidateTaxInvoiceSettings(model, out var validationError))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning!",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            if (!model.EnableTax)
            {
                model.GstPercent = 0;
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveTaxInvoiceSettings", model, Method.Post);
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-success",
                    Title = "Success!",
                    DisplayMessage = string.IsNullOrWhiteSpace(response?.Message)
                        ? "Tax and invoice settings saved successfully."
                        : response.Message
                });
            }
            catch (Exception ex)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Fail!",
                    DisplayMessage = BuildApiErrorMessage(ex, "Unable to save tax and invoice settings.")
                });

                return View(model);
            }

            return RedirectToAction(nameof(TaxInvoiceSettings));
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
                var response = await _clientApi.SendRequestAsync<TaxInvoiceSettingsViewModel>("/GetTaxInvoiceSettings", parameters, Method.Get);
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

                    var profile = await _loginApi.SendRequestAsync<LoginIoResponse>("/TenantprofileDetails", parms, Method.Get);
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
                EnableTax = true,
                GstPercent = 18,
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
                    DisplayMessage = "Only admin users can access Barcode / Tag Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    DisplayMessage = "Only admin users can access Barcode / Tag Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            model ??= new BarcodeTagSettingsViewModel();
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
                    Title = "Warning!",
                    DisplayMessage = validationError
                });

                return View(model);
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveBarcodeTagSettings", model, Method.Post);
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
                    Title = "Fail!",
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
                var response = await _clientApi.SendRequestAsync<BarcodeTagSettingsViewModel>("/GetBarcodeTagSettings", parameters, Method.Get);
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

        private async Task EnsureOrderTagsFromSettingsAsync(OrderCompletionContextDto context)
        {
            if (context == null || context.Items == null || context.Items.Count == 0)
            {
                return;
            }

            var normalizedItems = context.Items.Where(x => x != null).ToList();
            if (normalizedItems.Count == 0)
            {
                return;
            }

            var fallbackTotalPieces = context.TotalPieces > 0
                ? context.TotalPieces
                : normalizedItems.Sum(x => x.Quantity > 0 ? x.Quantity : 1);

            var perPieceTagMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var pieceCursor = 1;

            foreach (var item in normalizedItems)
            {
                var qty = item.Quantity > 0 ? item.Quantity : 1;
                var itemTags = new List<string>(qty);
                for (var i = 0; i < qty; i++)
                {
                    itemTags.Add(BuildOrderTagNumber(context.OrderNo, pieceCursor));
                    pieceCursor++;
                }

                var key = (item.ItemName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = "__item_" + pieceCursor;
                }

                perPieceTagMap[key] = itemTags;
            }

            var tenantName = string.IsNullOrWhiteSpace(context.TenantName)
                ? HttpContext.Session.GetString("TenantName")
                : context.TenantName;

            var storeCode = string.IsNullOrWhiteSpace(context.StoreCode)
                ? HttpContext.Session.GetString("TenantStore")
                : context.StoreCode;

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return;
            }

            BarcodeTagSettingsViewModel settings;
            try
            {
                settings = await LoadBarcodeTagSettingsAsync(tenantName, storeCode);
            }
            catch
            {
                return;
            }

            if (settings == null || !settings.EnableTagging)
            {
                return;
            }

            var nextNumber = settings.NextTagNumber < 1 ? 1 : settings.NextTagNumber;
            var assigned = 0;

            foreach (var item in normalizedItems)
            {
                if (!string.IsNullOrWhiteSpace(item.TagNo))
                {
                    continue;
                }

                item.TagNo = BuildPrintableTagNumber(settings.TagPrefix, nextNumber, settings.TagNumberPadding);
                nextNumber++;
                assigned++;
            }

            if (assigned <= 0)
            {
                return;
            }

            settings.TenantName = tenantName;
            settings.StoreCode = storeCode;
            settings.NextTagNumber = nextNumber;

            try
            {
                await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveBarcodeTagSettings", settings, Method.Post);
            }
            catch
            {
                // Best-effort update. Printing should continue even when settings persistence fails.
            }
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
                    DisplayMessage = "Only admin users can access Workflow Status Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    DisplayMessage = "Only admin users can access Workflow Status Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    Title = "Warning!",
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
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveWorkflowStatusSettings", model, Method.Post);
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
                    Title = "Fail!",
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
                model = await _clientApi.SendRequestAsync<WorkflowStatusSettingsViewModel>("/GetWorkflowStatusSettings", parameters, Method.Get);
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
                    DisplayMessage = "Only admin users can access Payment Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            var model = await LoadPaymentSettingsAsync(tenantName, storeCode);
            return View(model);
        }

        // Quick-fix stub: loads payment settings for the given tenant/store.
        // Ideally this should call a proper API; for now return safe defaults to allow compilation.
        private async Task<Models.Admin.PaymentSettingsViewModel> LoadPaymentSettingsAsync(string tenantName, string storeCode)
        {
            await Task.CompletedTask;
            return new Models.Admin.PaymentSettingsViewModel
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                EnableCash = true,
                EnableUpi = true,
                EnableCard = true,
                EnableWallet = true,
                DefaultPaymentMode = "Cash",
                AllowPartialPayment = true,
                AllowCredit = false,
                CreditLimitAmount = 0,
                RoundOffPayableAmount = false,
                Notes = string.Empty
            };
        }

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
                    DisplayMessage = "Only admin users can access Payment Settings."
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    Title = "Warning!",
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
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SavePaymentSettings", model, Method.Post);
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
                    Title = "Fail!",
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                return Json(new { success = false, message = "Session expired. Please login again.", stores = new List<object>() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetAllStoreStatuses", new Dictionary<string, string>(), Method.Get);

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
                        tenantEmail = (x?["TenantEmail"] ?? x?["tenantEmail"])?.ToString()
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
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                }, Method.Post);

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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                var response = await _loginApi.SendRequestAsync<JObject>("/GetAllTenantApprovals", new Dictionary<string, string>(), Method.Get);

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
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                }, Method.Post);

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
        public IActionResult NewStoreCreation()
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = storeCode;
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
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            newStoreCode = newStoreCode?.Trim();
            if (string.IsNullOrWhiteSpace(newStoreCode))
            {
                return Json(new { success = false, message = "Please enter store code." });
            }

            try
            {
                var createRequest = new
                {
                    TenantName = tenantName,
                    StoreCode = newStoreCode
                };

                LoginIoResponse response = null;
                try
                {
                    response = await _loginApi.SendRequestAsync<LoginIoResponse>("/CreateTenantStore", createRequest, Method.Post);
                }
                catch (Exception ex)
                {
                    var message = ex?.InnerException?.Message ?? ex?.Message ?? string.Empty;
                    if (!message.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                        && !message.Contains("404", StringComparison.OrdinalIgnoreCase))
                    {
                        throw;
                    }

                    response = await _clientApi.SendRequestAsync<LoginIoResponse>("/CreateTenantStore", createRequest, Method.Post);
                }

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

        [HttpGet]
        public IActionResult ImportExportPricesCsv()
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = storeCode;
            return View();
        }

        [HttpGet]
        public IActionResult Integrations()
            => AdminOnlyView("Integrations", "Manage third-party integrations and external system connectors.");

        [HttpPost]
        public async Task<JsonResult> CreateStoreUser(string email, string password, string role = "StoreUser", string storeCode = null)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can create store users." });
            }

            var tenantEmail = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Json(new { success = false, message = "Email and password are required." });
            }

            if (!TryResolveTargetStore(currentStoreCode, storeCode, out var resolvedStoreCode, out var validationMessage))
            {
                return Json(new { success = false, message = validationMessage });
            }

            if (!string.Equals(resolvedStoreCode, currentStoreCode, StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Store users can only be created for the currently logged-in store." });
            }

            try
            {
                var normalizedEmail = NormalizeEmail(email);
                var storesToValidate = await GetTenantStoreCodesForValidationAsync(tenantEmail, currentStoreCode);
                if (!storesToValidate.Contains(resolvedStoreCode, StringComparer.OrdinalIgnoreCase))
                {
                    storesToValidate.Add(resolvedStoreCode);
                }

                if (storesToValidate.Count == 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Unable to validate existing store users. Please try again."
                    });
                }

                var isDuplicate = await IsStoreUserEmailExistsAcrossApplicationAsync(normalizedEmail, tenantEmail, storesToValidate);

                if (isDuplicate)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Store user already exists in another store."
                    });
                }

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

                var response = await _loginApi.SendRequestAsync<CustomerIOResponse>("/CreateStoreUser", request, Method.Post);
                return Json(new
                {
                    success = response != null && response.StatusCode == "200",
                    message = response?.Message ?? "Store user created successfully."
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
                    message = "Session expired. Please login again."
                });
            }

            try
            {
                var customersResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomers", BuildScopedParameters(("eMail", string.Empty)), RestSharp.Method.Get);

                var customerNames = (customersResponse?.CustomerNames ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var todayOrders = 0;
                var pendingPaymentCount = 0;
                decimal pendingPaymentAmount = 0;
                decimal advanceUsedToday = 0;
                var customersWithOrders = 0;
                var readyForDeliveryCount = 0;
                var pendingDeliveryCount = 0;

                var today = DateTime.UtcNow.Date;

                foreach (var customerName in customerNames)
                {
                    var customer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", customerName.Trim())), RestSharp.Method.Get);

                    if (customer == null || customer.StatusCode != "200" || string.IsNullOrWhiteSpace(customer.CustCode))
                    {
                        continue;
                    }

                    var orderResponse = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetLaundryOrders", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "custCode", customer.CustCode }
                    }, Method.Get);

                    var orders = orderResponse?.Orders ?? new List<LaundryOrderDto>();
                    if (orders.Count > 0)
                    {
                        customersWithOrders++;
                    }

                    foreach (var order in orders)
                    {
                        var orderDate = order.CreatedDate.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(order.CreatedDate, DateTimeKind.Utc)
                            : order.CreatedDate.ToUniversalTime();

                        if (orderDate.Date == today)
                        {
                            todayOrders++;

                            if (order.AdvanceUsed > 0)
                            {
                                advanceUsedToday += order.AdvanceUsed;
                            }
                        }

                        if (order.NetPayable > 0)
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
                }, Method.Get);

                if (deliveryStatus != null && deliveryStatus.StatusCode == "200")
                {
                    readyForDeliveryCount = deliveryStatus.ReadyForDeliveryCount;
                    pendingDeliveryCount = deliveryStatus.PendingDeliveryCount;
                }

                return Json(new
                {
                    success = true,
                    message = string.Empty,
                    todayOrders,
                    pendingPaymentCount,
                    pendingPaymentAmount = Math.Round(pendingPaymentAmount, 2, MidpointRounding.AwayFromZero),
                    walkinsToday = todayOrders,
                    advanceUsedToday = Math.Round(advanceUsedToday, 2, MidpointRounding.AwayFromZero),
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

        [HttpGet]
        public async Task<JsonResult> GetStoreUsers(string storeCode = null)
        {
            if (!IsAdminUser())
            {
                return Json(new { success = false, message = "Only admin users can view store users.", users = new List<object>() });
            }

            var tenantEmail = HttpContext.Session.GetString("TenantName");
            var currentStoreCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantEmail) || string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again.", users = new List<object>() });
            }

            if (!TryResolveTargetStore(currentStoreCode, storeCode, out var resolvedStoreCode, out var validationMessage))
            {
                return Json(new { success = false, message = validationMessage, users = new List<object>() });
            }

            if (!string.Equals(resolvedStoreCode, currentStoreCode, StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Store users can only be viewed for the currently logged-in store.", users = new List<object>() });
            }

            try
            {
                var response = await _loginApi.SendRequestAsync<JObject>("/GetStoreUsers", new Dictionary<string, string>
                {
                    { "tenantEmail", tenantEmail },
                    { "storeCode", resolvedStoreCode }
                }, Method.Get);

                var users = ExtractStoreUsersForUi(response);

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
                    createdDate = createdDateText
                });
            }

            return results;
        }

        [HttpGet]
        public IActionResult StoreUsersSetup()
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            ViewData["TenantName"] = tenantName;
            ViewData["StoreCode"] = currentStoreCode;
            ViewData["AllowedStores"] = new List<string> { currentStoreCode };
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

            model.TenantName = tenantName;
            model.StoreCode = storeCode;

            if (model.PickupReminderHours < 0 || model.PickupReminderHours > 168)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning!",
                    DisplayMessage = "Pickup reminder should be between 0 and 168 hours."
                });
                return View(model);
            }

            if (model.LoyaltyPointsPerOrder < 0 || model.LoyaltyPointsPerOrder > 100)
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert-warning",
                    Title = "Warning!",
                    DisplayMessage = "Loyalty points per order should be between 0 and 100."
                });
                return View(model);
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveCustomerPreferences", model, Method.Post);
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
                    Title = "Fail!",
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
                EnableMinimumOrder = true,
                MinimumOrderAmount = 100,
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

            if (openingTime == closingTime)
            {
                validationError = "Opening and closing time cannot be the same.";
                return false;
            }

            validationError = null;
            return true;
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

            var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomers", paramsGetAllStoresByClient, RestSharp.Method.Get);
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
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SearchCustomers", parameters, Method.Get);
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
                    Message = "Session expired. Please login again."
                });
            }

            var parameters = BuildScopedParameters(("customerName", customerName));

            CustomerIOResponse responseMessage;

            try
            {
                responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", parameters, Method.Get);
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
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter Customer Name" });
                return RedirectToAction("Index", "Customer");
            }

            if (string.IsNullOrWhiteSpace(customerInfoDto.Address))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter Customer Address" });
                return RedirectToAction("Index", "Customer");
            }

            if (!string.IsNullOrWhiteSpace(customerInfoDto.PhoneNumber) && !Regex.IsMatch(customerInfoDto.PhoneNumber.Trim().Replace(" ", string.Empty), @"^\d{7,15}$"))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter a valid phone number (7-15 digits)" });
                return RedirectToAction("Index", "Customer");
            }

            if (!string.IsNullOrWhiteSpace(customerInfoDto.Email) && !Regex.IsMatch(customerInfoDto.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert-warning", Title = "warning!", DisplayMessage = "Please enter a valid email address" });
                return RedirectToAction("Index", "Customer");
            }

            customerInfoDto.CustomerName = customerInfoDto.CustomerName?.Trim();
            customerInfoDto.Address = customerInfoDto.Address?.Trim();
            // MembershipId removed system-wide
            customerInfoDto.BarCode = string.IsNullOrWhiteSpace(customerInfoDto.BarCode) ? null : customerInfoDto.BarCode.Trim();
            customerInfoDto.PhoneNumber = string.IsNullOrWhiteSpace(customerInfoDto.PhoneNumber) ? null : customerInfoDto.PhoneNumber.Trim().Replace(" ", string.Empty);
            customerInfoDto.Email = string.IsNullOrWhiteSpace(customerInfoDto.Email) ? null : customerInfoDto.Email.Trim();

            string tenantName = HttpContext.Session.GetString("TenantName");
            string tenanStore = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(tenanStore))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert alert-danger", Title = "Fail!", DisplayMessage = "Session expired. Please login again." });
                return RedirectToAction("Index", "Login");
            }

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
                responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/InsertCustomer", createRequest, RestSharp.Method.Post);

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

                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto { CssClassName = "alert alert-danger", Title = "Fail!", DisplayMessage = failureMessage });
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
                    Title = "Fail!",
                    DisplayMessage = displayMessage
                });
                return RedirectToAction("Index", "Customer");
            }
        }

        [HttpPost]
        public async Task<JsonResult> UpdateCustomer(CustomerInfoDto customerInfoDto)
        {
            customerInfoDto.customerCode = customerInfoDto.customerCode?.Trim();

            if (string.IsNullOrWhiteSpace(customerInfoDto.customerCode) && !string.IsNullOrWhiteSpace(customerInfoDto.CustomerName))
            {
                var findParams = BuildScopedParameters(("customerName", customerInfoDto.CustomerName.Trim()));

                var existingCustomer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", findParams, Method.Get);
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

            if (!string.IsNullOrWhiteSpace(customerInfoDto.PhoneNumber) && !Regex.IsMatch(customerInfoDto.PhoneNumber.Trim().Replace(" ", string.Empty), @"^\d{7,15}$"))
            {
                return Json(new { success = false, message = "Please enter a valid phone number (7-15 digits)" });
            }

            if (!string.IsNullOrWhiteSpace(customerInfoDto.Email) && !Regex.IsMatch(customerInfoDto.Email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                return Json(new { success = false, message = "Please enter a valid email address" });
            }

            customerInfoDto.CustomerName = customerInfoDto.CustomerName?.Trim();
            customerInfoDto.Address = customerInfoDto.Address?.Trim();
            customerInfoDto.BarCode = string.IsNullOrWhiteSpace(customerInfoDto.BarCode) ? null : customerInfoDto.BarCode.Trim();
            customerInfoDto.PhoneNumber = string.IsNullOrWhiteSpace(customerInfoDto.PhoneNumber) ? null : customerInfoDto.PhoneNumber.Trim().Replace(" ", string.Empty);
            customerInfoDto.Email = string.IsNullOrWhiteSpace(customerInfoDto.Email) ? null : customerInfoDto.Email.Trim();

            customerInfoDto.StoreCode = HttpContext.Session.GetString("TenantStore");
            customerInfoDto.TenantName = HttpContext.Session.GetString("TenantName");

            if (string.IsNullOrWhiteSpace(customerInfoDto.StoreCode) || string.IsNullOrWhiteSpace(customerInfoDto.TenantName))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

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
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/UpdateCustomer", updateRequest, Method.Put);
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

                var existingCustomer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", findParams, Method.Get);
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
                var responseMessage = await _clientApi.SendRequestAsync<CustomerIOResponse>("/DeleteCustomer", parameters, Method.Delete);
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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

                var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveCustomerAdvance", saveRequest, Method.Post);
                TempData["CustomerAdvancesToast"] = string.IsNullOrWhiteSpace(saveResponse?.Message)
                    ? "Customer advance saved successfully."
                    : saveResponse.Message;
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
                    message = "Session expired. Please login again.",
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
                if (!string.IsNullOrWhiteSpace(customerName))
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
                }, Method.Get);

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
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
            }

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
                enabledPaymentModes = new List<string> { "Cash", "UPI", "Card", "Wallet" };
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

            var taxSettings = await LoadTaxInvoiceSettingsAsync(tenantName, storeCode);
            ViewData["TaxEnabled"] = taxSettings.EnableTax && taxSettings.GstPercent > 0;
            ViewData["GstPercent"] = taxSettings.GstPercent;
            ViewData["PricesIncludeTax"] = taxSettings.PricesIncludeTax;

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
        public async Task<IActionResult> SetupCreateOrder(LaundryOrderDto model)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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

            model.AdvanceUsed = Math.Round(model.AdvanceUsed, 2, MidpointRounding.AwayFromZero);
            var netPayableAfterAdvance = Math.Round(Math.Max(0m, model.OrderAmount - model.AdvanceUsed), 2, MidpointRounding.AwayFromZero);

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

                var saveResponse = await _clientApi.SendRequestAsync<JObject>("/CreateLaundryOrder", saveRequest, Method.Post);

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
                    TempData["SetupCreateOrderToast"] = $"Order created successfully. Order No: {orderNo}";
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
                        OrderAmount = model.OrderAmount,
                        TaxAmount = model.TaxAmount,
                        AdvanceUsed = model.AdvanceUsed,
                        PaidNow = model.PaidNow,
                        PendingAmount = model.PendingAmount,
                        NetPayable = model.PendingAmount,
                        PaymentMode = model.PaymentMode,
                        CreatedDate = DateTime.UtcNow,
                        TotalPieces = createdTotalPieces,
                        Items = responseItems
                    };

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
                ViewData["OrderMode"] = model.OrderMode;
                ViewData["SetupOnly"] = false;
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
        public async Task<JsonResult> SendOrderBillEmail(string orderNo, string email)
        {
            var context = await TryGetOrderCompletionContextAsync(orderNo, allowFallback: true);
            if (context == null || string.IsNullOrWhiteSpace(context.OrderNo))
            {
                return Json(new { success = false, message = "Order details not available. Please reload order and try again." });
            }

            var toEmail = string.IsNullOrWhiteSpace(email) ? (context.CustomerEmail ?? string.Empty) : email.Trim();
            if (string.IsNullOrWhiteSpace(toEmail))
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

            if (context != null && !string.IsNullOrWhiteSpace(context.OrderNo)
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
                TempData["OrderCompletionContext"] = JsonConvert.SerializeObject(fallback);
            }

            return fallback;
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
                }, Method.Get);

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
                    TaxAmount = order.TaxAmount,
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

            string getConfig(params string[] keys)
            {
                bool isPlaceholder(string value)
                {
                    var v = (value ?? string.Empty).Trim();
                    return string.Equals(v, "yourgmail@gmail.com", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v, "your-16-char-app-password", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v, "your-smtp-host", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v, "your-user", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v, "your-password", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v, "noreply@yourdomain.com", StringComparison.OrdinalIgnoreCase);
                }

                foreach (var key in keys)
                {
                    var value = _configuration[key];
                    if (!string.IsNullOrWhiteSpace(value) && !isPlaceholder(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var smtpHost = getConfig("Email:Smtp:Host", "MailConfiguration:SmtpServer");
            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                return (false, "SMTP settings are not configured. Configure Email:Smtp or MailConfiguration in appsettings.");
            }

            var smtpPortText = getConfig("Email:Smtp:Port", "MailConfiguration:Port");
            var smtpPort = int.TryParse(smtpPortText, out var p) ? p : 587;
            var smtpUser = getConfig("Email:Smtp:Username", "MailConfiguration:Username");
            var smtpPassword = getConfig("Email:Smtp:Password", "MailConfiguration:Password");
            var enableSslText = getConfig("Email:Smtp:EnableSsl");
            var enableSsl = string.IsNullOrWhiteSpace(enableSslText)
                ? smtpPort == 465 || smtpPort == 587
                : !string.Equals(enableSslText, "false", StringComparison.OrdinalIgnoreCase);
            var fromEmail = getConfig("Email:Smtp:From", "MailConfiguration:MailFrom", "MailConfiguration:Sender");

            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                fromEmail = smtpUser;
            }

            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                return (false, "Email sender is not configured. Set Email:Smtp:From, MailConfiguration:MailFrom, or SMTP username.");
            }

            var portsToTry = new List<int> { smtpPort };
            if (string.Equals(smtpHost, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!portsToTry.Contains(587))
                {
                    portsToTry.Add(587);
                }

                if (!portsToTry.Contains(465))
                {
                    portsToTry.Add(465);
                }
            }

            var billLines = BuildBillLines(context);
            var body = string.Join("\n", billLines);
            var pdfBytes = BuildStyledInvoicePdf(context);
            var invoiceNo = string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo;

            Exception lastException = null;

            foreach (var portToTry in portsToTry)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(MailboxAddress.Parse(fromEmail));
                    message.To.Add(MailboxAddress.Parse(toEmail));
                    message.Subject = $"Laundry Invoice {invoiceNo}";

                    var builder = new BodyBuilder
                    {
                        TextBody = body
                    };

                    builder.Attachments.Add($"Invoice-{invoiceNo}.pdf", pdfBytes, MimeKit.ContentType.Parse("application/pdf"));
                    message.Body = builder.ToMessageBody();

                    using (var smtp = new MailKit.Net.Smtp.SmtpClient())
                    {
                        smtp.Timeout = 30000;

                        var socketOption = enableSsl
                            ? (portToTry == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                            : SecureSocketOptions.None;
                        await smtp.ConnectAsync(smtpHost, portToTry, socketOption);

                        if (!string.IsNullOrWhiteSpace(smtpUser))
                        {
                            await smtp.AuthenticateAsync(smtpUser, smtpPassword ?? string.Empty);
                        }

                        await smtp.SendAsync(message);
                        await smtp.DisconnectAsync(true);
                    }

                    return (true, "Bill emailed successfully.");
                }
                catch (Exception ex)
                {
                    lastException ??= ex;
                }
            }

            return (false, BuildApiErrorMessage(lastException ?? new Exception("SMTP send failed."), "Unable to send bill email."));
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
            catch
            {
                // Do not block order flow when audit insert fails.
            }
        }

        private static List<string> BuildBillLines(OrderCompletionContextDto context)
        {
            var lines = new List<string>
            {
                $"Tenant: {context.TenantName}",
                $"Store: {context.StoreCode}",
                $"Invoice No: {(string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo)}",
                $"Order No: {context.OrderNo}",
                $"Customer: {context.CustomerName} ({context.CustCode})",
                $"Date (UTC): {(context.CreatedDate == default ? DateTime.UtcNow : context.CreatedDate):yyyy-MM-dd HH:mm}",
                "",
                "Items:"
            };

            if (context.Items != null && context.Items.Count > 0)
            {
                var idx = 1;
                foreach (var item in context.Items)
                {
                    lines.Add($"{idx}. {item.ItemName}  Qty:{item.Quantity}  Unit:{item.UnitPrice:0.00}  Tag:{item.TagNo}");
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
            lines.Add($"Order Amount: {context.OrderAmount:0.00}");
            lines.Add($"Tax: {context.TaxAmount:0.00}");
            lines.Add($"Advance Utilized: {context.AdvanceUsed:0.00}");
            lines.Add($"Paid Now: {context.PaidNow:0.00}");
            lines.Add($"Pending Amount: {context.PendingAmount:0.00}");
            lines.Add($"Net Payable: {context.NetPayable:0.00}");

            return lines;
        }

        private static byte[] BuildStyledInvoicePdf(OrderCompletionContextDto context)
        {
            context ??= new OrderCompletionContextDto();
            context.Items ??= new List<LaundryOrderItemDto>();

            var invoiceNo = string.IsNullOrWhiteSpace(context.InvoiceNo) ? context.OrderNo : context.InvoiceNo;
            var issueDate = context.CreatedDate == default ? DateTime.UtcNow : context.CreatedDate;
            var dueDate = issueDate;
            var subTotal = context.OrderAmount;
            var tax = context.TaxAmount;
            var advanceUsed = context.AdvanceUsed;
            var totalDue = context.NetPayable;

            var tenantNameRaw = (context.TenantName ?? string.Empty).Trim();
            var companyName = string.IsNullOrWhiteSpace(tenantNameRaw)
                ? "YOUR COMPANY NAME"
                : (tenantNameRaw.Contains("@") ? tenantNameRaw.Split('@')[0].Replace('.', ' ').Replace('_', ' ') : tenantNameRaw);

            QuestPDF.Settings.License = LicenseType.Community;

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
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(item.UnitPrice.ToString("0.00"));
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(item.Quantity.ToString());
                                    table.Cell().BorderBottom(1).BorderColor("#edf1f6").Padding(6).AlignRight().Text(lineTotal.ToString("0.00"));
                                }
                            }
                            else
                            {
                                table.Cell().ColumnSpan(4).BorderBottom(1).BorderColor("#edf1f6").Padding(6).Text("No line items available.");
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

                            sumRow("Subtotal", subTotal.ToString("0.00"));
                            sumRow("Tax", tax.ToString("0.00"));
                            sumRow("Advance Utilized (-)", advanceUsed.ToString("0.00"));
                            sumRow("Paid Now (-)", context.PaidNow.ToString("0.00"));
                            sumRow("Pending Amount", context.PendingAmount.ToString("0.00"));
                            sumRow("Total Due", totalDue.ToString("0.00"), true);
                        });

                        col.Item().Border(1).BorderColor("#dbe3ec").Padding(8).Text($"Payment Information: {context.OrderMode} / {context.ServiceType} / Advance Used: {context.AdvanceUsed:0.00} / Paid Now: {context.PaidNow:0.00} / Pending: {context.PendingAmount:0.00}");
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
                    message = "Session expired. Please login again.",
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
                }, Method.Get);

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
                    message = "Session expired. Please login again."
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
                }, Method.Get);

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
                return Json(new List<string>());
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
                }, Method.Get);

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
        public IActionResult OrderWorkflow()
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                TempData["UserMessage"] = JsonConvert.SerializeObject(new MessageDto
                {
                    CssClassName = "alert alert-danger",
                    Title = "Fail!",
                    DisplayMessage = "Session expired. Please login again."
                });
                return RedirectToAction("Index", "Login");
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

            ViewData["WorkflowTenantName"] = tenantName;
            ViewData["WorkflowStoreCode"] = storeCode;
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
                    message = "Session expired. Please login again.",
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
                var response = await _clientApi.SendRequestAsync<LaundryOrderResponseDto>("/GetStoreLaundryOrders", new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "status", status ?? string.Empty },
                    { "searchText", searchText ?? string.Empty }
                }, Method.Get);

                var orders = response?.Orders ?? new List<LaundryOrderDto>();

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
            catch (Exception ex)
            {
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
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                    }, Method.Get);

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
                }, Method.Post);

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
        public async Task<JsonResult> SettleOrderPendingPayment(string orderNo, decimal paidAmount, string paymentMode = null, string notes = null)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                }, Method.Post);

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
                return "Received";
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

            var customer = await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", normalizedName)), Method.Get);

            if (customer != null && customer.StatusCode == "200" && !string.IsNullOrWhiteSpace(customer.CustCode))
            {
                return customer;
            }

            var searchResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SearchCustomers", BuildScopedParameters(("searchText", normalizedName)), Method.Get);

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

            return await _clientApi.SendRequestAsync<CustomerIOResponse>("/GetCustomerByName", BuildScopedParameters(("customerName", bestMatch.Trim())), Method.Get);
        }

        [HttpPost]
        public async Task<JsonResult> SaveLaundryItemPrice(LaundryItemPriceDto model)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveLaundryItemPrice", request, Method.Post);
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
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/DeactivateLaundryItemPrice", request, Method.Post);

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
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/ReactivateLaundryItemPrice", request, Method.Post);

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
        public async Task<JsonResult> GetLaundryItemPrices([FromQuery] bool includeInactive = false)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new
                {
                    success = false,
                    message = "Session expired. Please login again.",
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
                }, Method.Get);

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
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            try
            {
                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/ImportDefaultLaundryItemPrices",
                    new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "overwriteExisting", overwriteExisting ? "true" : "false" }
                    }, Method.Post);

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
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            if (csvFile == null || csvFile.Length == 0)
            {
                return Json(new { success = false, message = "Please choose a CSV file to import." });
            }

            try
            {
                var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!overwriteExisting)
                {
                    var existingItems = await _clientApi.SendRequestAsync<List<LaundryItemPriceDto>>("/GetLaundryItemPrices", new Dictionary<string, string>
                    {
                        { "tenantName", tenantName },
                        { "storeCode", storeCode },
                        { "includeInactive", "true" }
                    }, Method.Get) ?? new List<LaundryItemPriceDto>();

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

                        if (rowNumber == 1
                            && string.Equals(serviceType, "ServiceType", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(category, "Category", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(itemName, "ItemName", StringComparison.OrdinalIgnoreCase))
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
                            continue;
                        }

                        var key = BuildLaundryItemKey(serviceType, category, itemName);
                        if (!overwriteExisting && existingKeys.Contains(key))
                        {
                            skippedCount++;
                            skippedLineNumbers.Add(rowNumber);
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

                        var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveLaundryItemPrice", request, Method.Post);
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
                    message = $"CSV import completed. Imported: {importedCount}, Skipped: {skippedCount}, Failed: {failedCount}.",
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
                return Json(new { success = false, message = "Session expired. Please login again." });
            }

            try
            {
                var query = new Dictionary<string, string>
                {
                    { "tenantName", tenantName },
                    { "storeCode", storeCode },
                    { "includeInactive", includeInactive ? "true" : "false" }
                };

                var services = await _clientApi.SendRequestAsync<List<StoreServiceMasterDto>>("/GetStoreServiceMasters", query, Method.Get)
                               ?? new List<StoreServiceMasterDto>();
                var items = await _clientApi.SendRequestAsync<List<StoreItemMasterDto>>("/GetStoreItemMasters", query, Method.Get)
                            ?? new List<StoreItemMasterDto>();

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
                return Json(new { success = false, message = "Session expired. Please login again." });
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

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreServiceMaster", request, Method.Post);
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
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                }, Method.Get) ?? new List<StoreServiceMasterDto>();

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

                        var saveResponse = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreServiceMaster", request, Method.Post);
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
        public async Task<JsonResult> SetStoreServiceMasterStatus(string masterType, string name, bool isActive)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                    IsActive = isActive
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SetStoreServiceMasterStatus", request, Method.Post);
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
                return Json(new { success = false, message = "Session expired. Please login again." });
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

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SaveStoreItemMaster", request, Method.Post);
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
        public async Task<JsonResult> SetStoreItemMasterStatus(string serviceType, string category, string itemName, bool isActive)
        {
            var tenantName = HttpContext.Session.GetString("TenantName");
            var storeCode = HttpContext.Session.GetString("TenantStore");

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return Json(new { success = false, message = "Session expired. Please login again." });
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
                    IsActive = isActive
                };

                var response = await _clientApi.SendRequestAsync<CustomerIOResponse>("/SetStoreItemMasterStatus", request, Method.Post);
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

        private static string BuildLaundryItemKey(string serviceType, string category, string itemName)
        {
            return $"{serviceType?.Trim().ToLowerInvariant()}|{category?.Trim().ToLowerInvariant()}|{itemName?.Trim().ToLowerInvariant()}";
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
                // ignored - fallback below
            }

            return $"{fallbackMessage} {rawMessage}";
        }
    }
}