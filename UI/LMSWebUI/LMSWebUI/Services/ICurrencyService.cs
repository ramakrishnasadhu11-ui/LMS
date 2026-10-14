using System.Threading.Tasks;

namespace LMSWebUI.Services
{
    public class CurrencyConfig
    {
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyLocale { get; set; }
        public int DecimalDigits { get; set; }
    }

    public interface ICurrencyService
    {
        Task<CurrencyConfig> GetEffectiveCurrencyAsync(string tenantEmail, string storeCode);
        string Format(decimal amount, CurrencyConfig config);
    }
}
