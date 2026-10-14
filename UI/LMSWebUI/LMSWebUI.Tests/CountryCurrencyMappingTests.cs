using Xunit;
using LMSWebUI.Services;

namespace LMSWebUI.Tests
{
    public class CountryCurrencyMappingTests
    {
        [Fact]
        public void India_Defaults_To_INR()
        {
            var cfg = CountryCurrencyMapping.GetDefaultForCountry("India");
            Assert.NotNull(cfg);
            Assert.Equal("INR", cfg.CurrencyCode);
            Assert.Equal("₹", cfg.CurrencySymbol);
        }

        [Fact]
        public void UnknownCountry_Returns_Null()
        {
            var cfg = CountryCurrencyMapping.GetDefaultForCountry("Atlantis");
            Assert.Null(cfg);
        }
    }
}
