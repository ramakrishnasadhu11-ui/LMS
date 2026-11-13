using System;
using System.Globalization;

namespace LMSWebUI.Services
{
    public static class CurrencyFormatting
    {
        public static string FormatAmount(decimal value, CurrencyConfig currencyCfg, ICurrencyService currencyService)
        {
            try
            {
                var effectiveCfg = currencyCfg;
                if (currencyService != null)
                {
                    return currencyService.Format(value, effectiveCfg);
                }

                if (!CurrencyService.IsValid(effectiveCfg))
                {
                    return "Currency configuration unavailable";
                }

                // Fallback to local formatting only for an explicitly supplied valid configuration.
                var c = effectiveCfg;
                var culture = new CultureInfo(c.CurrencyLocale ?? "en-IN");
                var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
                nfi.CurrencySymbol = c.CurrencySymbol;
                nfi.CurrencyDecimalDigits = c.DecimalDigits;
                return value.ToString("C", nfi);
            }
            catch
            {
                return "Currency configuration unavailable";
            }
        }
    }
}
