using System.Collections.Generic;
using System.Linq;

namespace LMS.Identity.BusinessSerive.Services
{
    internal class CountryProfile
    {
        public string CountryCode { get; set; }

        public string CountryName { get; set; }

        public string CurrencyCode { get; set; }

        public string CurrencySymbol { get; set; }

        public string CurrencyLocale { get; set; }

        public int? DecimalDigits { get; set; }

        public string TimeZoneIana { get; set; }

        public string DialingCode { get; set; }

        public string TaxProfileCode { get; set; }
    }

    internal static class CountryCurrencyMapping
    {
        // Phase A centralized country profile defaults (configurable overrides remain supported).
        private static readonly IReadOnlyList<CountryProfile> _profiles = new List<CountryProfile>
        {
            new CountryProfile
            {
                CountryCode = "IN",
                CountryName = "India",
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                CurrencyLocale = "en-IN",
                DecimalDigits = 2,
                TimeZoneIana = "Asia/Kolkata",
                DialingCode = "+91",
                TaxProfileCode = "IN_GST"
            },
            new CountryProfile
            {
                CountryCode = "US",
                CountryName = "United States",
                CurrencyCode = "USD",
                CurrencySymbol = "$",
                CurrencyLocale = "en-US",
                DecimalDigits = 2,
                TimeZoneIana = "America/New_York",
                DialingCode = "+1",
                TaxProfileCode = "US_SALES_TAX"
            },
            new CountryProfile
            {
                CountryCode = "GB",
                CountryName = "United Kingdom",
                CurrencyCode = "GBP",
                CurrencySymbol = "£",
                CurrencyLocale = "en-GB",
                DecimalDigits = 2,
                TimeZoneIana = "Europe/London",
                DialingCode = "+44",
                TaxProfileCode = "GB_VAT"
            },
            new CountryProfile
            {
                CountryCode = "AE",
                CountryName = "United Arab Emirates",
                CurrencyCode = "AED",
                CurrencySymbol = "د.إ",
                CurrencyLocale = "en-AE",
                DecimalDigits = 2,
                TimeZoneIana = "Asia/Dubai",
                DialingCode = "+971",
                TaxProfileCode = "AE_VAT"
            },
            new CountryProfile
            {
                CountryCode = "DE",
                CountryName = "Germany",
                CurrencyCode = "EUR",
                CurrencySymbol = "€",
                CurrencyLocale = "de-DE",
                DecimalDigits = 2,
                TimeZoneIana = "Europe/Berlin",
                DialingCode = "+49",
                TaxProfileCode = "DE_VAT"
            },
            new CountryProfile
            {
                CountryCode = "FR",
                CountryName = "France",
                CurrencyCode = "EUR",
                CurrencySymbol = "€",
                CurrencyLocale = "fr-FR",
                DecimalDigits = 2,
                TimeZoneIana = "Europe/Paris",
                DialingCode = "+33",
                TaxProfileCode = "FR_VAT"
            }
        };

        private static readonly Dictionary<string, CountryProfile> _byCode = _profiles
            .ToDictionary(x => x.CountryCode, x => x, System.StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, CountryProfile> _byName = _profiles
            .ToDictionary(x => x.CountryName, x => x, System.StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<CountryProfile> GetProfiles()
        {
            return _profiles;
        }

        public static CountryProfile GetDefaultForCountryCode(string countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                return null;
            }

            _byCode.TryGetValue(countryCode.Trim(), out var profile);
            return profile;
        }

        public static CountryProfile GetDefaultForCountry(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return null;
            _byName.TryGetValue(country.Trim(), out var profile);
            return profile;
        }

        public static CountryProfile Resolve(string countryCode, string countryName)
        {
            var byCode = GetDefaultForCountryCode(countryCode);
            if (byCode != null)
            {
                return byCode;
            }

            return GetDefaultForCountry(countryName);
        }
    }
}
