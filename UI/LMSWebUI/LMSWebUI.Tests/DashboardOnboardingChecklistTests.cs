using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LMSWebUI.Controllers;
using LMSWebUI.Models;
using LMSWebUI.Models.Customer;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using RestSharp;
using Xunit;

namespace LMSWebUI.Tests
{
    public class DashboardOnboardingChecklistTests
    {
        [Fact]
        public async Task ResolveOnboardingChecklistProgress_LaundryPrices_UsesSelectedTenantStore_AndMarksCompleted_FromWrappedItemsResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                LaundryItemPricesResponse = new List<LaundryItemPriceDto>
                {
                    new LaundryItemPriceDto
                    {
                        TenantName = "tenant@example.com",
                        StoreCode = "S1",
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        UnitPrice = 80.00m,
                        IsActive = true
                    }
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "tenant@example.com", "S1");

            Assert.True(progress.IsLaundryPricesCompleted);

            Assert.NotNull(customerApi.LastLaundryItemPricesQuery);
            Assert.Equal("tenant@example.com", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "tenantName"));
            Assert.Equal("S1", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "storeCode"));
            Assert.Equal("false", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "includeInactive"), ignoreCase: true);
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_BarcodeTags_UsesSelectedTenantStore_AndMarksCompleted_FromMasterResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                BarcodeTagSettingsResponse = new JObject
                {
                    ["TenantName"] = "idia.laundry@example.com",
                    ["StoreCode"] = "IDIA01",
                    ["EnableTagging"] = true,
                    ["TagPrefix"] = "IDL",
                    ["NextTagNumber"] = 12,
                    ["TagNumberPadding"] = 4,
                    ["ResetTagNumberYearly"] = false
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsBarcodeTagsCompleted);

            Assert.NotNull(customerApi.LastBarcodeTagSettingsQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastBarcodeTagSettingsQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastBarcodeTagSettingsQuery, "storeCode"));
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_FirstCustomer_UsesSelectedTenantStore_AndMarksCompleted_FromMasterCustomersResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                CustomersResponse = new JObject
                {
                    ["CustomerNames"] = new JArray("Idia Walk-In")
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsFirstCustomerCompleted);

            Assert.NotNull(customerApi.LastCustomersQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastCustomersQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastCustomersQuery, "storeCode"));
            Assert.Equal(string.Empty, GetQueryValue(customerApi.LastCustomersQuery, "eMail"));
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_FirstOrder_UsesSelectedTenantStore_AndMarksCompleted_FromMasterOrdersResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                CustomersResponse = new JObject
                {
                    ["CustomerNames"] = new JArray("Idia Walk-In")
                },
                CustomerByNameResponse = new JObject
                {
                    ["CustCode"] = "CUST-001"
                },
                LaundryOrdersResponse = new JObject
                {
                    ["Orders"] = new JArray(
                        new JObject
                        {
                            ["OrderNo"] = "ORD-0001",
                            ["CustCode"] = "CUST-001"
                        })
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsFirstOrderCompleted);

            Assert.NotNull(customerApi.LastCustomerByNameQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastCustomerByNameQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastCustomerByNameQuery, "storeCode"));
            Assert.Equal("Idia Walk-In", GetQueryValue(customerApi.LastCustomerByNameQuery, "customerName"));

            Assert.NotNull(customerApi.LastLaundryOrdersQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastLaundryOrdersQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastLaundryOrdersQuery, "storeCode"));
            Assert.Equal("CUST-001", GetQueryValue(customerApi.LastLaundryOrdersQuery, "custCode"));
        }

        [Fact]
        public async Task Index_SuperAdmin_WithEmptyTenantStore_AllowsDashboardView()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "superadmin@example.com", string.Empty, "SuperAdmin");

            var result = await controller.Index();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Super Admin Dashboard", controller.ViewData["DashboardTitle"]);
            Assert.Equal("SuperAdmin", controller.ViewData["UserRole"]);
            Assert.False((bool)controller.ViewData["CanResumeOnboarding"]);
            Assert.NotNull(viewResult.Model);
        }

        [Fact]
        public async Task Index_TrialActiveTenant_PopulatesTrialExpiryAndRemainingDays_ForDashboardNotification()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "trial-active@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(15).Date.AddHours(10)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "trial-active@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "trial-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Trial", model.SubscriptionStatus);
            Assert.True(model.TrialEndUtc.HasValue);
            Assert.Equal(loginApi.TenantProfileResponse.TrialEndUtc.Value.ToUniversalTime(), model.TrialEndUtc.Value.ToUniversalTime());
            Assert.True(model.TrialDaysRemaining.HasValue);
            Assert.True(model.TrialDaysRemaining.Value <= 15 && model.TrialDaysRemaining.Value >= 14);
        }

        [Fact]
        public async Task Index_ActivePaidTenant_DoesNotPopulateTrialNotificationState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "paid-active@example.com",
                    PlanCode = "Premium",
                    SubscriptionStatus = "Active",
                    TrialEndUtc = DateTime.UtcNow.AddDays(10)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "paid-active@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "paid-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Active", model.SubscriptionStatus);
            Assert.Null(model.TrialEndUtc);
            Assert.Null(model.TrialDaysRemaining);
        }

        [Fact]
        public async Task Index_AdminTrialActiveTenant_ShowsTrialNotificationBanner_WithExactExpiryAndRemainingDays()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "admin-trial@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(7).Date.AddHours(9)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "admin-trial@example.com",
                            ["StoreCode"] = "UQO",
                            ["IsActive"] = true
                        })
                }
            };

            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "admin-trial@example.com", "UQO", "Admin");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Trial", model.SubscriptionStatus);
            Assert.True(model.TrialEndUtc.HasValue);
            Assert.True(model.TrialDaysRemaining.HasValue);
            Assert.True(model.TrialDaysRemaining.Value <= 7 && model.TrialDaysRemaining.Value >= 6);

            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);
            Assert.Contains("id=\"dashboardTrialNotification\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("var showTrialNotification = !isSuperAdminUser", viewContents, StringComparison.Ordinal);
        }

        private static async Task<(bool IsBusinessStoreSettingsCompleted, bool IsLaundryPricesCompleted, bool IsBarcodeTagsCompleted, bool IsFirstCustomerCompleted, bool IsFirstOrderCompleted)> InvokeChecklistProgressAsync(
            DashboardController controller,
            string tenantName,
            string tenantStore)
        {
            var method = typeof(DashboardController).GetMethod(
                "ResolveOnboardingChecklistProgressAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var task = method.Invoke(controller, new object[] { tenantName, tenantStore }) as Task<(bool, bool, bool, bool, bool)>;
            Assert.NotNull(task);
            return await task;
        }

        private sealed class InMemoryLoginApiClient : ILoginApiClient
        {
            public LoginIoResponse TenantProfileResponse { get; set; }
            public JObject StoreStatusesResponse { get; set; } = new JObject();

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (typeof(T) == typeof(LoginIoResponse)
                    && string.Equals(requestUrl, "/TenantprofileDetails", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult((T)(object)(TenantProfileResponse ?? new LoginIoResponse()));
                }

                if (typeof(T) == typeof(JObject)
                    && string.Equals(requestUrl, "/GetAllStoreStatuses", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult((T)(object)(StoreStatusesResponse ?? new JObject()));
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());
        }

        private static string GetQueryValue(Dictionary<string, string> query, string key)
        {
            return query.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private static void AttachSession(Controller controller, string tenantName, string tenantStore, string userRole)
        {
            var httpContext = new DefaultHttpContext();
            var session = new TestSession();

            session.Set("TenantName", Encoding.UTF8.GetBytes(tenantName ?? string.Empty));
            session.Set("TenantStore", Encoding.UTF8.GetBytes(tenantStore ?? string.Empty));
            session.Set("UserRole", Encoding.UTF8.GetBytes(userRole ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(tenantStore))
            {
                session.Set("TenantAllowedStores", Encoding.UTF8.GetBytes(tenantStore));
            }

            httpContext.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        }

        private sealed class TestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>();

            public bool IsAvailable => true;
            public string Id { get; } = Guid.NewGuid().ToString();
            public IEnumerable<string> Keys => _store.Keys;

            public void Clear() => _store.Clear();

            public Task CommitAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task LoadAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Remove(string key) => _store.Remove(key);

            public void Set(string key, byte[] value) => _store[key] = value;

            public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value);
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public ISession Session { get; set; }
        }

        private sealed class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context)
                => new Dictionary<string, object>();

            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
            }
        }

        private sealed class FakeWebHostEnvironment : IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMSWebUI.Tests";
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        }

        private sealed class InMemoryDashboardCustomerApiClient : ICustomerApiClient
        {
            public List<LaundryItemPriceDto> LaundryItemPricesResponse { get; set; } = new List<LaundryItemPriceDto>();
            public Dictionary<string, string> LastLaundryItemPricesQuery { get; private set; }
            public JObject BarcodeTagSettingsResponse { get; set; }
            public Dictionary<string, string> LastBarcodeTagSettingsQuery { get; private set; }
            public JObject CustomersResponse { get; set; }
            public Dictionary<string, string> LastCustomersQuery { get; private set; }
            public JObject CustomerByNameResponse { get; set; }
            public Dictionary<string, string> LastCustomerByNameQuery { get; private set; }
            public JObject LaundryOrdersResponse { get; set; }
            public Dictionary<string, string> LastLaundryOrdersQuery { get; private set; }

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (string.Equals(requestUrl, "/GetLaundryItemPrices", StringComparison.OrdinalIgnoreCase))
                {
                    LastLaundryItemPricesQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(List<LaundryItemPriceDto>))
                    {
                        return Task.FromResult((T)(object)LaundryItemPricesResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetBarcodeTagSettings", StringComparison.OrdinalIgnoreCase))
                {
                    LastBarcodeTagSettingsQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)BarcodeTagSettingsResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetCustomers", StringComparison.OrdinalIgnoreCase))
                {
                    LastCustomersQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)CustomersResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetCustomerByName", StringComparison.OrdinalIgnoreCase))
                {
                    LastCustomerByNameQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)CustomerByNameResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetLaundryOrders", StringComparison.OrdinalIgnoreCase))
                {
                    LastLaundryOrdersQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)LaundryOrdersResponse);
                    }
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());
        }
    }
}
