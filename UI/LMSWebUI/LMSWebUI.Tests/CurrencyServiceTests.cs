using System.Threading.Tasks;
using Xunit;
using LMSWebUI.Services;
using LMSWebUI.Models.Admin;
using Microsoft.Extensions.Caching.Memory;

namespace LMSWebUI.Tests
{
    class FakeStoreConfigService : IStoreConfigurationService
    {
        private readonly StoreConfigurationViewModel _storeCfg;
        private readonly StoreConfigurationViewModel _tenantCfg;

        public int StoreCalls { get; private set; }
        public int TenantCalls { get; private set; }

        public FakeStoreConfigService(StoreConfigurationViewModel storeCfg, StoreConfigurationViewModel tenantCfg)
        {
            _storeCfg = storeCfg;
            _tenantCfg = tenantCfg;
        }

        public Task<StoreConfigurationViewModel> LoadAsync(string tenantName, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                TenantCalls++;
                if (_tenantCfg != null)
                {
                    _tenantCfg.IsExistingRecord = !string.IsNullOrWhiteSpace(_tenantCfg.CurrencyCode)
                        || !string.IsNullOrWhiteSpace(_tenantCfg.CurrencySymbol)
                        || !string.IsNullOrWhiteSpace(_tenantCfg.CurrencyLocale)
                        || _tenantCfg.CurrencyDecimalDigits != null;
                }
                return Task.FromResult(_tenantCfg);
            }

            StoreCalls++;
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

    public class CurrencyServiceTests
    {
        [Fact]
        public async Task StoreOverride_TakesPrecedence()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "INR", CurrencySymbol = "₹", CurrencyLocale = "en-IN", CurrencyDecimalDigits = 2 };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var resolved = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("USD", resolved.CurrencyCode);
            Assert.Equal("$", resolved.CurrencySymbol);
            Assert.Equal("en-US", resolved.CurrencyLocale);
            Assert.Equal(2, resolved.DecimalDigits);
        }

        [Fact]
        public async Task TenantFallback_WhenStoreMissing()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = null, CurrencySymbol = null, CurrencyLocale = null, CurrencyDecimalDigits = null };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var resolved = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("EUR", resolved.CurrencyCode);
            Assert.Equal("€", resolved.CurrencySymbol);
            Assert.Equal("fr-FR", resolved.CurrencyLocale);
        }

        [Fact]
        public async Task LegacyFallback_WhenNoneConfigured()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = null };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = null };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var resolved = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("INR", resolved.CurrencyCode);
        }

        [Fact]
        public async Task Cache_IsUsed_ForSubsequentCalls()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "INR" };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var r1 = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            var r2 = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal(1, fake.StoreCalls);
            Assert.Equal(r1.CurrencyCode, r2.CurrencyCode);
        }
    }
}
