using Microsoft.Extensions.Caching.Memory;

namespace LMSWebUI.Services
{
    public static class CurrencyCacheKeys
    {
        public static string TenantVersionKey(string tenant) => $"currency::version::{tenant ?? string.Empty}";

        public static string CurrencyKey(IMemoryCache cache, string tenant, string store)
        {
            string ver = string.Empty;
            try
            {
                if (cache != null && cache.TryGetValue(TenantVersionKey(tenant), out string v))
                {
                    ver = v ?? string.Empty;
                }
            }
            catch
            {
                ver = string.Empty;
            }

            return $"currency::{tenant ?? string.Empty}::{store ?? string.Empty}::v:{ver}";
        }
    }
}
