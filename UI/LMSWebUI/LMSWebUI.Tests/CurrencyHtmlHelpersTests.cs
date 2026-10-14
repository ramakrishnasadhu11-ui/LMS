using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using LMSWebUI.Helpers;
using LMSWebUI.Services;
using LMSWebUI.Models.Admin;

namespace LMSWebUI.Tests
{
    // Minimal ISession implementation for tests
    class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value);
    }

    // Use Moq to provide IHtmlHelper with ViewContext in tests; no concrete implementation needed here.

    // Minimal store config service for tests
    class SimpleStoreConfigService : IStoreConfigurationService
    {
        private readonly StoreConfigurationViewModel _storeCfg;
        private readonly StoreConfigurationViewModel _tenantCfg;
        public SimpleStoreConfigService(StoreConfigurationViewModel storeCfg, StoreConfigurationViewModel tenantCfg)
        {
            _storeCfg = storeCfg ?? new StoreConfigurationViewModel();
            _tenantCfg = tenantCfg ?? new StoreConfigurationViewModel();
        }
        public Task<StoreConfigurationViewModel> LoadAsync(string tenantName, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                if (_tenantCfg != null)
                {
                    _tenantCfg.IsExistingRecord = !string.IsNullOrWhiteSpace(_tenantCfg.CurrencyCode)
                        || !string.IsNullOrWhiteSpace(_tenantCfg.CurrencySymbol)
                        || !string.IsNullOrWhiteSpace(_tenantCfg.CurrencyLocale)
                        || _tenantCfg.CurrencyDecimalDigits != null;
                }
                return Task.FromResult(_tenantCfg);
            }

            if (_storeCfg != null)
            {
                _storeCfg.IsExistingRecord = !string.IsNullOrWhiteSpace(_storeCfg.CurrencyCode)
                    || !string.IsNullOrWhiteSpace(_storeCfg.CurrencySymbol)
                    || !string.IsNullOrWhiteSpace(_storeCfg.CurrencyLocale)
                    || _storeCfg.CurrencyDecimalDigits != null;
            }
            return Task.FromResult(_storeCfg);
        }
        public Task<(bool Success, string Message)> SaveAsync(StoreConfigurationViewModel model) => Task.FromResult((true, "ok"));
        public StoreConfigurationViewModel Normalize(StoreConfigurationViewModel model) => model;
        public bool HasData(StoreConfigurationViewModel model) => model != null;
    }

    public class CurrencyHtmlHelpersTests
    {
        private IHtmlHelper CreateHtmlHelperWithServices(ICurrencyService currencyService, string tenantName = "tenant@example.com", string storeCode = "S1")
        {
            var httpContext = new DefaultHttpContext();
            var session = new TestSession();
            session.Set("TenantName", System.Text.Encoding.UTF8.GetBytes(tenantName));
            session.Set("TenantStore", System.Text.Encoding.UTF8.GetBytes(storeCode));
            httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = session });
            var services = new ServiceCollection();
            services.AddSingleton(typeof(ICurrencyService), currencyService);
            services.AddSingleton<IMemoryCache, MemoryCache>();
            httpContext.RequestServices = services.BuildServiceProvider();

            var vc = new ViewContext();
            vc.HttpContext = httpContext;

            var mock = new Moq.Mock<IHtmlHelper>();
            mock.SetupGet(m => m.ViewContext).Returns(vc);
            return mock.Object;
        }

        class SessionFeature : ISessionFeature { public ISession Session { get; set; } = null!; }

        [Fact]
        public async Task FormatCurrency_NullInput_ReturnsEmpty()
        {
            var fakeStore = new SimpleStoreConfigService(null, null);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fakeStore, cache);
            var helper = CreateHtmlHelperWithServices(svc);

            var result = CurrencyHtmlHelpers.FormatCurrency(helper, null);
            Assert.Equal(string.Empty, result.ToString());
            await Task.CompletedTask;
        }

        [Fact]
        public async Task FormatCurrency_PositiveAndNegative_UsesServiceFormat()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "INR", CurrencySymbol = "₹", CurrencyLocale = "en-IN", CurrencyDecimalDigits = 2 };
            var fake = new SimpleStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);
            var helper = CreateHtmlHelperWithServices(svc);

            var positive = (await CurrencyHtmlHelpers.FormatCurrencyAsync(helper, 123.45m)).ToString();
            var cfg = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            var expected = svc.Format(123.45m, cfg);
            Assert.Equal(expected, positive);

            var negative = (await CurrencyHtmlHelpers.FormatCurrencyAsync(helper, -5.5m)).ToString();
            var expectedNeg = svc.Format(-5.5m, cfg);
            Assert.Equal(expectedNeg, negative);
        }

        [Fact]
        public async Task FormatCurrency_TenantFallbackAndLegacyFallback()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = null, CurrencySymbol = null, CurrencyLocale = null, CurrencyDecimalDigits = null };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 };
            var fake = new SimpleStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);
            var helper = CreateHtmlHelperWithServices(svc);

            var formatted = (await CurrencyHtmlHelpers.FormatCurrencyAsync(helper, 10m)).ToString();
            var cfg = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            var expected = svc.Format(10m, cfg);
            Assert.Equal(expected, formatted);

            // Legacy fallback when both null
            var fake2 = new SimpleStoreConfigService(new StoreConfigurationViewModel { CurrencyCode = null }, new StoreConfigurationViewModel { CurrencyCode = null });
            var svc2 = new CurrencyService(fake2, cache);
            var helper2 = CreateHtmlHelperWithServices(svc2);
            var formatted2 = (await CurrencyHtmlHelpers.FormatCurrencyAsync(helper2, 5m)).ToString();
            var cfg2 = await svc2.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            var expected2 = svc2.Format(5m, cfg2);
            Assert.Equal(expected2, formatted2);
        }
    }
}
