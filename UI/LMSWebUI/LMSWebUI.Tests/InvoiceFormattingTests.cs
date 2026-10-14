using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using LMSWebUI.Services;
using LMSWebUI.Models.Admin;

namespace LMSWebUI.Tests
{
    public class InvoiceFormattingTests
    {
        [Fact]
        public async Task FormatAmount_PositiveZeroNegative_UsesCurrencyService()
        {
            var storeCfg = new StoreConfigurationViewModel { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var tenantCfg = new StoreConfigurationViewModel { CurrencyCode = null };
            var fake = new SimpleStoreConfigService(storeCfg, tenantCfg);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var svc = new CurrencyService(fake, cache);

            var cfg = await svc.GetEffectiveCurrencyAsync("tenant@example.com", "S1");

            var pos = CurrencyFormatting.FormatAmount(123.45m, new CurrencyConfig { CurrencyCode = cfg.CurrencyCode, CurrencySymbol = cfg.CurrencySymbol, CurrencyLocale = cfg.CurrencyLocale, DecimalDigits = cfg.DecimalDigits }, svc);
            Assert.Equal(svc.Format(123.45m, cfg), pos);

            var zero = CurrencyFormatting.FormatAmount(0m, cfg, svc);
            Assert.Equal(svc.Format(0m, cfg), zero);

            var neg = CurrencyFormatting.FormatAmount(-5.5m, cfg, svc);
            Assert.Equal(svc.Format(-5.5m, cfg), neg);
        }
    }
}
