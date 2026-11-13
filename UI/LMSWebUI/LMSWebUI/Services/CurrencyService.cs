using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LMSWebUI.Models;
using LMSWebUI.Models.Admin;
using Microsoft.Extensions.Caching.Memory;

namespace LMSWebUI.Services
{
    public class CurrencyService : ICurrencyService
    {
        private readonly IStoreConfigurationService _storeConfigurationService;
        private readonly IMemoryCache _cache;
        private readonly ILoginApiClient _loginApi;

        public CurrencyService(IStoreConfigurationService storeConfigurationService, IMemoryCache cache, ILoginApiClient loginApi = null)
        {
            _storeConfigurationService = storeConfigurationService ?? throw new ArgumentNullException(nameof(storeConfigurationService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _loginApi = loginApi;
        }

        public async Task<CurrencyConfig> GetEffectiveCurrencyAsync(string tenantEmail, string storeCode)
        {
            var result = await ResolveAsync(tenantEmail, storeCode);
            return result.IsResolved ? result.Currency : null;
        }

        public async Task<CurrencyResolutionResult> ResolveAsync(string tenantEmail, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(tenantEmail))
            {
                return Unresolved("Tenant identity is required before currency can be resolved.");
            }

            var cacheKey = CurrencyCacheKeys.CurrencyKey(_cache, tenantEmail, storeCode);
            if (_cache.TryGetValue(cacheKey, out CurrencyResolutionResult cached))
            {
                return cached;
            }

            StoreConfigurationViewModel store = null;
            StoreConfigurationViewModel tenant = null;
            try { store = await _storeConfigurationService.LoadAsync(tenantEmail, storeCode); } catch { }
            try { tenant = await _storeConfigurationService.LoadAsync(tenantEmail, string.Empty); } catch { }

            var country = FirstNonEmpty(store?.Country, tenant?.Country);
            var countryCode = NormalizeCountryCode(FirstNonEmpty(GetCountryCode(store), GetCountryCode(tenant)));
            if (string.IsNullOrWhiteSpace(country) && string.IsNullOrWhiteSpace(countryCode) && _loginApi != null)
            {
                try
                {
                    var profile = await _loginApi.SendRequestAsync<LoginIoResponse>(
                        "/TenantprofileDetails",
                        new Dictionary<string, string> { { "eMail", tenantEmail } },
                        RestSharp.Method.GET);
                    country = FirstNonEmpty(profile?.Country);
                    countryCode = NormalizeCountryCode(FirstNonEmpty(profile?.CountryCode));
                }
                catch { }
            }

            var countryDefault = CountryCurrencyMapping.GetDefaultForCountry(countryCode)
                                 ?? CountryCurrencyMapping.GetDefaultForCountry(country);

            var resolved = TryBuild(store, tenant, countryDefault, true)
                           ?? TryBuild(null, tenant, countryDefault, true)
                           ?? countryDefault;

            var result = resolved == null
                ? Unresolved(BuildWarning(country, countryCode))
                : new CurrencyResolutionResult
                {
                    Currency = resolved,
                    IsResolved = true,
                    Source = HasCurrency(store) ? "Store" : HasCurrency(tenant) ? "Tenant" : "CountryDefault",
                    Country = country,
                    CountryCode = countryCode
                };

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
            return result;
        }

        public string Format(decimal amount, CurrencyConfig config)
        {
            if (!IsValid(config))
            {
                throw new InvalidOperationException("A resolved currency configuration is required to format monetary values.");
            }

            var culture = new CultureInfo(config.CurrencyLocale);
            var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
            nfi.CurrencySymbol = config.CurrencySymbol;
            nfi.CurrencyDecimalDigits = config.DecimalDigits;
            return amount.ToString("C", nfi);
        }

        internal static bool IsValid(CurrencyConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.CurrencyCode)
                || !Regex.IsMatch(config.CurrencyCode.Trim(), "^[A-Za-z]{3}$")
                || string.IsNullOrWhiteSpace(config.CurrencySymbol)
                || string.IsNullOrWhiteSpace(config.CurrencyLocale)
                || config.DecimalDigits < 0 || config.DecimalDigits > 6)
            {
                return false;
            }

            try { _ = new CultureInfo(config.CurrencyLocale); return true; }
            catch { return false; }
        }

        private static CurrencyConfig TryBuild(StoreConfigurationViewModel store, StoreConfigurationViewModel tenant, CurrencyConfig country, bool allowCountry)
        {
            if (!HasCurrency(store) && !HasCurrency(tenant)) return null;
            var config = new CurrencyConfig
            {
                CountryCode = FirstNonEmpty(store?.Country, tenant?.Country, country?.CountryCode),
                CurrencyCode = FirstValidCode(store?.CurrencyCode, tenant?.CurrencyCode, allowCountry ? country?.CurrencyCode : null),
                CurrencySymbol = FirstNonEmpty(store?.CurrencySymbol, tenant?.CurrencySymbol, allowCountry ? country?.CurrencySymbol : null),
                CurrencyLocale = FirstValidLocale(store?.CurrencyLocale, tenant?.CurrencyLocale, allowCountry ? country?.CurrencyLocale : null),
                DecimalDigits = FirstValidDigits(store?.CurrencyDecimalDigits, tenant?.CurrencyDecimalDigits, country?.DecimalDigits)
            };
            config.CurrencyCode = config.CurrencyCode?.Trim().ToUpperInvariant();
            return IsValid(config) ? config : null;
        }

        private static bool HasCurrency(StoreConfigurationViewModel model) => model?.IsExistingRecord == true
            && (!string.IsNullOrWhiteSpace(model.CurrencyCode) || !string.IsNullOrWhiteSpace(model.CurrencySymbol)
                || !string.IsNullOrWhiteSpace(model.CurrencyLocale) || model.CurrencyDecimalDigits.HasValue);

        private static string GetCountryCode(StoreConfigurationViewModel model) => model?.CountryCode;

        private static string FirstNonEmpty(params string[] values) => values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

        private static string FirstValidCode(params string[] values) => values?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && Regex.IsMatch(x.Trim(), "^[A-Za-z]{3}$"))?.Trim();

        private static string FirstValidLocale(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                try { _ = new CultureInfo(value.Trim()); return value.Trim(); } catch { }
            }
            return null;
        }

        private static int FirstValidDigits(int? store, int? tenant, int? country)
        {
            foreach (var value in new[] { store, tenant, country })
            {
                if (value.HasValue && value.Value >= 0 && value.Value <= 6) return value.Value;
            }
            return -1;
        }

        private static string NormalizeCountryCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var normalized = value.Trim().ToUpperInvariant();
            return normalized switch { "USA" => "US", "IND" => "IN", "GBR" => "GB", "ARE" => "AE", _ => normalized };
        }

        private static string BuildWarning(string country, string countryCode) => string.IsNullOrWhiteSpace(country) && string.IsNullOrWhiteSpace(countryCode)
            ? "Currency is not configured and the tenant country is unknown. Configure a tenant or store currency before displaying or creating orders."
            : $"Currency is not configured for country {countryCode ?? country}. Configure a valid tenant or store currency before displaying or creating orders.";

        private static CurrencyResolutionResult Unresolved(string warning) => new CurrencyResolutionResult
        {
            IsResolved = false,
            Warning = warning,
            Source = "Unresolved"
        };
    }
}