using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LMSWebUI.Controllers;
using LMSWebUI.Models.Customer;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

        private static string GetQueryValue(Dictionary<string, string> query, string key)
        {
            return query.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
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
