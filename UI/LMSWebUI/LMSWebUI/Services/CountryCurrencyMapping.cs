using System.Collections.Generic;

namespace LMSWebUI.Services
{
    // Centralized mapping from country name to default currency config.
    // Kept simple and configurable in-code for now; can be moved to IConfiguration later if needed.
    public static class CountryCurrencyMapping
    {
        public static IReadOnlyDictionary<string, CurrencyConfig> GetMappings()
        {
            return new Dictionary<string, CurrencyConfig>
            {
                { "India", new CurrencyConfig { CurrencyCode = "INR", CurrencySymbol = "₹", CurrencyLocale = "en-IN", DecimalDigits = 2 } },
                { "United States", new CurrencyConfig { CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", DecimalDigits = 2 } },
                { "United Kingdom", new CurrencyConfig { CurrencyCode = "GBP", CurrencySymbol = "£", CurrencyLocale = "en-GB", DecimalDigits = 2 } },
                { "France", new CurrencyConfig { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "fr-FR", DecimalDigits = 2 } },
                { "Germany", new CurrencyConfig { CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "de-DE", DecimalDigits = 2 } }
            };
        }

        public static CurrencyConfig GetDefaultForCountry(string country)
        {
            if (string.IsNullOrWhiteSpace(country)) return null;
            var maps = GetMappings();
            if (maps.TryGetValue(country.Trim(), out var cfg)) return cfg;
            return null;
        }
    }
}
