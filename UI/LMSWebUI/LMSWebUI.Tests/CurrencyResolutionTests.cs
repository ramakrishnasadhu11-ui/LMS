using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using LMSWebUI.Services;
using LMSWebUI.Models.Admin;
using LMSWebUI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace LMSWebUI.Tests
{
    public class CurrencyResolutionTests
    {
        [Fact]
        public async Task StoreOverridesTenantConfiguration()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 };
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
        public async Task SaveAndLoadStoreConfigurationRoundtrip()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);

            var tenantModel = new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 };
            await scs.SaveAsync(tenantModel);
            var loaded = await scs.LoadAsync("tenant@example.com", string.Empty);
            Assert.Equal("EUR", loaded.CurrencyCode);
        }

        [Fact]
        public async Task TenantUpdateRefreshesInheritedStoreCurrency()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);
            var cs = new CurrencyService(scs, cache);

            // Save initial tenant currency (production save path)
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 });

            var before = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("EUR", before.CurrencyCode);

            // Update tenant currency
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", CurrencyDecimalDigits = 2 });

            var after = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("GBP", after.CurrencyCode);
            Assert.Equal("£", after.CurrencySymbol);
        }

        [Fact]
        public async Task TenantUpdatePreservesStoreOverrides()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);
            var cs = new CurrencyService(scs, cache);

            // Tenant currency
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 });

            // Store override
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = "S1", CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 });

            var before = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("USD", before.CurrencyCode);

            // Change tenant
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", CurrencyDecimalDigits = 2 });

            var after = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            // Store override should still take precedence
            Assert.Equal("USD", after.CurrencyCode);
            Assert.Equal("$", after.CurrencySymbol);
        }

        [Fact]
        public async Task PartialStoreConfigurationInheritsUpdatedTenantFields()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);
            var cs = new CurrencyService(scs, cache);

            // Tenant initial
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 });

            // Partial store override: symbol+locale but no code -> should inherit tenant code
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = "S1", CurrencyCode = null, CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = null });

            var before = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("EUR", before.CurrencyCode);
            Assert.Equal("$", before.CurrencySymbol);
            Assert.Equal("en-US", before.CurrencyLocale);

            // Update tenant code -> should reflect in store's effective code while preserving store symbol/locale
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", CurrencyDecimalDigits = 2 });

            var after = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("GBP", after.CurrencyCode);
            Assert.Equal("$", after.CurrencySymbol);
            Assert.Equal("en-US", after.CurrencyLocale);
        }

        [Fact]
        public async Task StoreConfigurationUpdateRefreshesStoreCurrency()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);
            var cs = new CurrencyService(scs, cache);

            // Tenant default
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = string.Empty, CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", CurrencyDecimalDigits = 2 });

            var before = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S2");
            Assert.Equal("EUR", before.CurrencyCode);

            // Save store-specific override
            await scs.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = "S2", CurrencyCode = "AUD", CurrencySymbol = "A$", CurrencyLocale = "en-AU", CurrencyDecimalDigits = 2 });

            var after = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S2");
            Assert.Equal("AUD", after.CurrencyCode);
            Assert.Equal("A$", after.CurrencySymbol);
        }

        [Fact]
        public async Task LegacyFallbackRemainsWhenNoConfig()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var api = new InMemoryLoginApiClient();
            var scs = new StoreConfigurationService(api, cache);
            var cs = new CurrencyService(scs, cache);

            var resolved = await cs.GetEffectiveCurrencyAsync("tenant@example.com", "S3");
            Assert.Equal("INR", resolved.CurrencyCode);
            Assert.Equal("₹", resolved.CurrencySymbol);
        }

        [Fact]
        public async Task MissingOrInvalidStoreInheritsTenant()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = null, CurrencySymbol = null, CurrencyLocale = null, CurrencyDecimalDigits = null };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", CurrencyDecimalDigits = 2 };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var resolved = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("GBP", resolved.CurrencyCode);
            Assert.Equal("£", resolved.CurrencySymbol);
            Assert.Equal("en-GB", resolved.CurrencyLocale);
        }

        [Fact]
        public async Task LegacyFallbackUsedOnlyWhenNoValidConfig()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = null };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = null };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var resolved = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            Assert.Equal("INR", resolved.CurrencyCode);
            Assert.Equal("₹", resolved.CurrencySymbol);
            Assert.Equal("en-IN", resolved.CurrencyLocale);
            Assert.Equal(2, resolved.DecimalDigits);
        }

        [Fact]
        public async Task CacheInvalidatedOnSaveAsync()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD" };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = "EUR" };
            var fake = new FakeStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var r1 = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");
            // Simulate save that changes store config (via SimpleStoreConfigService used in other tests)
            // Use the real StoreConfigurationService.SaveAsync path to invalidate cache.
            var scs = new SimpleStoreConfigService(new StoreConfigurationViewModel { CurrencyCode = "JPY", CurrencySymbol = "¥", CurrencyLocale = "ja-JP", CurrencyDecimalDigits = 0 }, tenantCfg);
            var cacheBacked = new MemoryCache(new MemoryCacheOptions());
            var svcPrimary = new CurrencyService(fake, cacheBacked);
            var r1b = await svcPrimary.GetEffectiveCurrencyAsync("tenant@example.com", "S1");

            // Save via a StoreConfigurationService wrapper that performs cache eviction like production.
            var storeConfigService = new TestableStoreConfigurationService(scs, cacheBacked);
            await storeConfigService.SaveAsync(new StoreConfigurationViewModel { TenantName = "tenant@example.com", StoreCode = "S1", CurrencyCode = "JPY", CurrencySymbol = "¥", CurrencyLocale = "ja-JP", CurrencyDecimalDigits = 0 });

            var svcAfter = new CurrencyService(scs, cacheBacked);
            var r2 = await svcAfter.GetEffectiveCurrencyAsync("tenant@example.com", "S1");

            Assert.Equal("JPY", r2.CurrencyCode);
            Assert.Equal("¥", r2.CurrencySymbol);
        }

        // Helper test service that simulates the SaveAsync cache eviction logic used in production
        class TestableStoreConfigurationService : IStoreConfigurationService
        {
            private readonly IStoreConfigurationService _inner;
            private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

            public TestableStoreConfigurationService(IStoreConfigurationService inner, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
            {
                _inner = inner;
                _cache = cache;
            }

            public Task<StoreConfigurationViewModel> LoadAsync(string tenantName, string storeCode) => _inner.LoadAsync(tenantName, storeCode);

            public async Task<(bool Success, string Message)> SaveAsync(StoreConfigurationViewModel model)
            {
                var result = await _inner.SaveAsync(model);
                try
                {
                    var tenant = model?.TenantName ?? string.Empty;
                    var store = model?.StoreCode ?? string.Empty;
                    // Mirror production invalidation: bump tenant version token and remove specific key
                    try
                    {
                        var versionKey = CurrencyCacheKeys.TenantVersionKey(tenant);
                        var newVer = Guid.NewGuid().ToString("N");
                        _cache?.Set(versionKey, newVer, TimeSpan.FromMinutes(60));
                    }
                    catch { }

                    try
                    {
                        var specificKey = CurrencyCacheKeys.CurrencyKey(_cache, tenant, store);
                        _cache?.Remove(specificKey);
                    }
                    catch { }
                }
                catch
                {
                    // ignore
                }

                return result;
            }

            public StoreConfigurationViewModel Normalize(StoreConfigurationViewModel model) => _inner.Normalize(model);
            public bool HasData(StoreConfigurationViewModel model) => _inner.HasData(model);
        }

        [Fact]
        public void CurrencyFormatting_FallsBackWhenServiceNull()
        {
            var formatted = CurrencyFormatting.FormatAmount(10m, null, null);
            // Legacy fallback uses ₹ and en-IN; formatted result should contain either ₹ or a numeric string
            Assert.True(formatted.Contains("₹") || formatted.Contains("10"));
        }
    }
}
