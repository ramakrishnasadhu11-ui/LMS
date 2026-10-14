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

                // Fallback to local formatting when service is not available
                var c = effectiveCfg ?? new CurrencyConfig { CurrencyCode = "INR", CurrencySymbol = "₹", CurrencyLocale = "en-IN", DecimalDigits = 2 };
                var culture = new CultureInfo(c.CurrencyLocale ?? "en-IN");
                var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
                nfi.CurrencySymbol = c.CurrencySymbol ?? "₹";
                nfi.CurrencyDecimalDigits = c.DecimalDigits;
                return value.ToString("C", nfi);
            }
            catch
            {
                var sym = currencyCfg?.CurrencySymbol ?? "₹";
                var d = currencyCfg?.DecimalDigits ?? 2;
                return sym + " " + Math.Round(value, d).ToString($"N{d}", CultureInfo.InvariantCulture);
            }
        }
    }
}
