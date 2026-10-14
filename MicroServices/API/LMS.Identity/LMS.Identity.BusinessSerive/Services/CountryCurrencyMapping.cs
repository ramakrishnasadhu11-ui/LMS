using System.Collections.Generic;

namespace LMS.Identity.BusinessSerive.Services
{
    internal class CurrencyConfig
    {
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyLocale { get; set; }
        public int? DecimalDigits { get; set; }
    }

    internal static class CountryCurrencyMapping
    {
        // Minimal in-process mapping; kept small to avoid external dependency changes.
        private static readonly Dictionary<string, CurrencyConfig> _map = new()
        {
            ["India"] = new CurrencyConfig { CurrencyCode = "INR", CurrencySymbol = "₹", CurrencyLocale = "en-IN", DecimalDigits = 2 },
            ["United States"] = new CurrencyConfig { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", DecimalDigits = 2 },
            ["United Kingdom"] = new CurrencyConfig { CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", DecimalDigits = 2 },
            ["France"] = new CurrencyConfig { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", DecimalDigits = 2 },
            ["Germany"] = new CurrencyConfig { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "de-DE", DecimalDigits = 2 }
        };

        public static CurrencyConfig GetDefaultForCountry(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return null;
            _map.TryGetValue(country.Trim(), out var cfg);
            return cfg;
        }
    }
}
