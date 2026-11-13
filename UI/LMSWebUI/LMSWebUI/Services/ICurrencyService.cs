using System.Threading.Tasks;

namespace LMSWebUI.Services
{
    public class CurrencyConfig
    {
        public string CountryCode { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyLocale { get; set; }
        public int DecimalDigits { get; set; }
    }

    public sealed class CurrencyResolutionResult
    {
        public CurrencyConfig Currency { get; set; }
        public bool IsResolved { get; set; }
        public string Source { get; set; }
        public string Warning { get; set; }
        public string CountryCode { get; set; }
        public string Country { get; set; }
    }

    public interface ICurrencyService
    {
        Task<CurrencyConfig> GetEffectiveCurrencyAsync(string tenantEmail, string storeCode);
        Task<CurrencyResolutionResult> ResolveAsync(string tenantEmail, string storeCode);
        string Format(decimal amount, CurrencyConfig config);
    }
}
