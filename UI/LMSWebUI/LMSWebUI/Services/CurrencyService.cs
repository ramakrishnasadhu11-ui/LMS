using System;
using System.Globalization;
using System.Threading.Tasks;
using LMSWebUI.Models.Admin;
using Microsoft.Extensions.Caching.Memory;

namespace LMSWebUI.Services
{
    public class CurrencyService : ICurrencyService
    {
        private readonly IStoreConfigurationService _storeConfigurationService;
        private readonly IMemoryCache _cache;
        private static readonly CurrencyConfig LegacyDefault = new CurrencyConfig
        {
            CurrencyCode = "INR",
            CurrencySymbol = "₹",
            CurrencyLocale = "en-IN",
            DecimalDigits = 2
        };

        public CurrencyService(IStoreConfigurationService storeConfigurationService, IMemoryCache cache)
        {
            _storeConfigurationService = storeConfigurationService ?? throw new ArgumentNullException(nameof(storeConfigurationService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<CurrencyConfig> GetEffectiveCurrencyAsync(string tenantEmail, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail)) throw new ArgumentNullException(nameof(tenantEmail));

            var cacheKey = CurrencyCacheKeys.CurrencyKey(_cache, tenantEmail ?? string.Empty, storeCode ?? string.Empty);
            if (_cache.TryGetValue(cacheKey, out CurrencyConfig cached))
            {
                return cached;
            }

            // First try store-specific configuration. If store provides only a subset of fields
            // the effective configuration should inherit missing valid fields from tenant-level
            // configuration rather than defaulting to the legacy fallback.
            LMSWebUI.Models.Admin.StoreConfigurationViewModel tenantCfgFromStoreStage = null;
            try
            {
                var storeConfig = await _storeConfigurationService.LoadAsync(tenantEmail, storeCode);
                // Only treat storeConfig as an overriding configuration if it's an existing saved record.
                if (storeConfig != null && storeConfig.IsExistingRecord)
                {
                    var code = storeConfig.CurrencyCode?.Trim();
                    var symbol = storeConfig.CurrencySymbol?.Trim();
                    var locale = storeConfig.CurrencyLocale?.Trim();
                    int digits = storeConfig.CurrencyDecimalDigits ?? int.MinValue; // sentinel

                    bool codeValid = !string.IsNullOrWhiteSpace(code) && System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z]{3}$");
                    bool localeValid = true;
                    if (!string.IsNullOrWhiteSpace(locale))
                    {
                        try { var _ = new CultureInfo(locale); }
                        catch { localeValid = false; }
                    }

                    // Determine if store intends to override (either provides a valid ISO code or provides both symbol and a valid locale)
                    if (codeValid || (!string.IsNullOrWhiteSpace(symbol) && localeValid))
                    {
                        // sanitize digits
                        if (digits == int.MinValue) digits = LegacyDefault.DecimalDigits;
                        if (digits < 0 || digits > 6) digits = LegacyDefault.DecimalDigits;

                        // If store config is partial, attempt to load tenant-level configuration to inherit missing values
                        try
                        {
                            tenantCfgFromStoreStage = await _storeConfigurationService.LoadAsync(tenantEmail, string.Empty);
                        }
                        catch
                        {
                            tenantCfgFromStoreStage = null;
                        }

                        // Tenant-level validated values
                        string tenantCode = tenantCfgFromStoreStage?.CurrencyCode?.Trim();
                        string tenantSymbol = tenantCfgFromStoreStage?.CurrencySymbol?.Trim();
                        string tenantLocale = tenantCfgFromStoreStage?.CurrencyLocale?.Trim();
                        int tenantDigits = tenantCfgFromStoreStage?.CurrencyDecimalDigits ?? LegacyDefault.DecimalDigits;

                        bool tenantCodeValid = !string.IsNullOrWhiteSpace(tenantCode) && System.Text.RegularExpressions.Regex.IsMatch(tenantCode, "^[A-Za-z]{3}$");
                        bool tenantLocaleValid = true;
                        if (!string.IsNullOrWhiteSpace(tenantLocale))
                        {
                            try { var _ = new CultureInfo(tenantLocale); }
                            catch { tenantLocaleValid = false; }
                        }

                        var resolved = new CurrencyConfig
                        {
                            CurrencyCode = codeValid ? code.ToUpperInvariant() : (tenantCodeValid ? tenantCode.ToUpperInvariant() : LegacyDefault.CurrencyCode),
                            CurrencySymbol = !string.IsNullOrWhiteSpace(symbol) ? symbol : (!string.IsNullOrWhiteSpace(tenantSymbol) ? tenantSymbol : LegacyDefault.CurrencySymbol),
                            CurrencyLocale = (localeValid && !string.IsNullOrWhiteSpace(locale)) ? locale : (tenantLocaleValid && !string.IsNullOrWhiteSpace(tenantLocale) ? tenantLocale : LegacyDefault.CurrencyLocale),
                            DecimalDigits = digits != int.MinValue ? digits : (tenantCfgFromStoreStage?.CurrencyDecimalDigits ?? LegacyDefault.DecimalDigits)
                        };

                        _cache.Set(cacheKey, resolved, TimeSpan.FromMinutes(15));
                        return resolved;
                    }
                }
            }
            catch
            {
                // Ignore and try tenant-level below
            }

            // Try tenant-level configuration by calling LoadAsync with empty storeCode
            try
            {
                var tenantConfig = await _storeConfigurationService.LoadAsync(tenantEmail, string.Empty);
                if (tenantConfig != null)
                {
                    var code = tenantConfig.CurrencyCode?.Trim();
                    var symbol = tenantConfig.CurrencySymbol?.Trim();
                    var locale = tenantConfig.CurrencyLocale?.Trim();
                    int digits = tenantConfig.CurrencyDecimalDigits ?? LegacyDefault.DecimalDigits;

                    bool codeValid = !string.IsNullOrWhiteSpace(code) && System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z]{3}$");
                    bool localeValid = true;
                    if (!string.IsNullOrWhiteSpace(locale))
                    {
                        try { var _ = new CultureInfo(locale); }
                        catch { localeValid = false; }
                    }

                    if (codeValid || (!string.IsNullOrWhiteSpace(symbol) && localeValid))
                    {
                        if (digits < 0 || digits > 6) digits = LegacyDefault.DecimalDigits;

                        var resolved = new CurrencyConfig
                        {
                            CurrencyCode = codeValid ? code.ToUpperInvariant() : LegacyDefault.CurrencyCode,
                            CurrencySymbol = string.IsNullOrWhiteSpace(symbol) ? LegacyDefault.CurrencySymbol : symbol,
                            CurrencyLocale = localeValid && !string.IsNullOrWhiteSpace(locale) ? locale : LegacyDefault.CurrencyLocale,
                            DecimalDigits = digits
                        };

                        _cache.Set(cacheKey, resolved, TimeSpan.FromMinutes(15));
                        return resolved;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Final fallback: legacy default to preserve existing behavior
            _cache.Set(cacheKey, LegacyDefault, TimeSpan.FromMinutes(15));
            return LegacyDefault;
        }

        public string Format(decimal amount, CurrencyConfig config)
        {
            if (config == null) config = LegacyDefault;

            try
            {
                var culture = new CultureInfo(config.CurrencyLocale ?? LegacyDefault.CurrencyLocale);
                // clone number format to set decimal digits and currency symbol
                var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
                nfi.CurrencySymbol = config.CurrencySymbol ?? LegacyDefault.CurrencySymbol;
                nfi.CurrencyDecimalDigits = config.DecimalDigits;
                return amount.ToString("C", nfi);
            }
            catch
            {
                // If invalid locale or other error, fall back to a safe formatted string
                var symbol = config?.CurrencySymbol ?? LegacyDefault.CurrencySymbol;
                var digits = config?.DecimalDigits ?? LegacyDefault.DecimalDigits;
                return symbol + " " + Math.Round(amount, digits).ToString($"N{digits}", CultureInfo.InvariantCulture);
            }
        }
    }
}
