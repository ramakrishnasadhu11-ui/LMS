using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using LMS.Master.Utilities;
using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerServiceTaxModeResolutionTests : IClassFixture<RazorpaySqlServerFixture>
    {
        private readonly RazorpaySqlServerFixture _fixture;

        public CustomerServiceTaxModeResolutionTests(RazorpaySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpsertTaxConfiguration_MissingCountryCode_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var result = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = "GST",
                CountryCode = null,
                CurrencyCode = "INR"
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("CountryCode must be a valid 2-letter ISO code.", response.Message);
        }

        [Fact]
        public async Task UpsertTaxConfiguration_UnsupportedCountryCode_IsCurrentlyAccepted_Gap()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var upsert = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = "VAT",
                CountryCode = "ZZ",
                CurrencyCode = "ZZD"
            });

            Assert.Equal(HttpStatusCode.BadRequest, upsert.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(upsert.ResultSet);
            Assert.Equal("CountryCode is not supported. Supported countries: IN, US, GB, AE, DE, FR.", response.Message);
        }

        [Theory]
        [InlineData("IN", "USD", "INR")]
        [InlineData("US", "INR", "USD")]
        [InlineData("GB", "EUR", "GBP")]
        [InlineData("AE", "USD", "AED")]
        [InlineData("DE", "GBP", "EUR")]
        [InlineData("FR", "AED", "EUR")]
        public async Task UpsertTaxConfiguration_InvalidCurrencyForCountry_ReturnsBadRequest(string countryCode, string invalidCurrency, string expectedCurrency)
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var result = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = "VAT",
                CountryCode = countryCode,
                CurrencyCode = invalidCurrency
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal($"CurrencyCode for country {countryCode} must be {expectedCurrency}.", response.Message);
        }

        [Theory]
        [InlineData("IN", "GST", "INR")]
        [InlineData("US", "SalesTax", "USD")]
        [InlineData("GB", "VAT", "GBP")]
        [InlineData("AE", "VAT", "AED")]
        [InlineData("DE", "VAT", "EUR")]
        [InlineData("FR", "VAT", "EUR")]
        public async Task SupportedCountryProfiles_ReturnConfiguredTaxMode_AndCurrency(string countryCode, string taxMode, string currencyCode)
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var upsert = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = taxMode,
                CountryCode = countryCode,
                CurrencyCode = currencyCode,
                EnableTax = taxMode != "NoTax",
                GstPercent = taxMode == "GST" ? 18 : 0,
                IsPricesInclusive = true
            });

            Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

            var resolve = await service.ResolveTaxMode(tenant, store);
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = Assert.IsType<TaxModeResolutionDto>(resolve.ResultSet);
            Assert.Equal(taxMode, resolved.ResolvedTaxMode);
            Assert.Equal("CountryConfig", resolved.ResolutionSource);
            Assert.Equal(countryCode, resolved.CountryCode);

            var get = await service.GetTaxConfiguration(tenant, store);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var dto = Assert.IsType<TaxConfigurationDto>(get.ResultSet);
            Assert.Equal(countryCode, dto.CountryCode);
            Assert.Equal(currencyCode, dto.CurrencyCode);
            Assert.Equal(taxMode, dto.TaxMode);
        }

        [Theory]
        [InlineData("IN", "GST", "INR")]
        [InlineData("US", "SalesTax", "USD")]
        [InlineData("GB", "VAT", "GBP")]
        [InlineData("AE", "VAT", "AED")]
        [InlineData("DE", "VAT", "EUR")]
        [InlineData("FR", "VAT", "EUR")]
        public async Task SupportedCountryProfiles_ApplyDefaults_WhenTaxModeAndCurrencyMissing(string countryCode, string expectedTaxMode, string expectedCurrency)
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var upsert = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = null,
                CountryCode = countryCode,
                CurrencyCode = null,
                EnableTax = true,
                GstPercent = expectedTaxMode == "GST" ? 18 : 0
            });

            Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

            var resolve = await service.ResolveTaxMode(tenant, store);
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = Assert.IsType<TaxModeResolutionDto>(resolve.ResultSet);
            Assert.Equal(expectedTaxMode, resolved.ResolvedTaxMode);
            Assert.Equal("CountryConfig", resolved.ResolutionSource);

            var get = await service.GetTaxConfiguration(tenant, store);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var dto = Assert.IsType<TaxConfigurationDto>(get.ResultSet);
            Assert.Equal(expectedTaxMode, dto.TaxMode);
            Assert.Equal(expectedCurrency, dto.CurrencyCode);
        }

        [Fact]
        public async Task NoTax_ConfiguredMode_OverridesLegacyTaxFlags()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var upsert = await service.UpsertTaxConfiguration(new TaxConfigurationUpsertRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TaxMode = "NoTax",
                CountryCode = "US",
                CurrencyCode = "USD",
                EnableTax = true,
                GstPercent = 18
            });

            Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

            var resolve = await service.ResolveTaxMode(tenant, store);
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = Assert.IsType<TaxModeResolutionDto>(resolve.ResultSet);
            Assert.Equal("NoTax", resolved.ResolvedTaxMode);
            Assert.Equal("CountryConfig", resolved.ResolutionSource);
        }

        [Fact]
        public async Task ResolveTaxMode_CountryDerivedForIndia_WhenTaxModeMissing()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            context.TaxInvoiceSettings.Add(new TaxInvoiceSettingsEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableTax = true,
                GstPercent = 18,
                PricesIncludeTax = false,
                InvoicePrefix = "INV",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                TaxMode = null,
                CountryCode = "IN",
                CurrencyCode = "INR",
                IsPricesInclusive = false,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var service = _fixture.CreateService(context);
            var resolve = await service.ResolveTaxMode(tenant, store);

            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = Assert.IsType<TaxModeResolutionDto>(resolve.ResultSet);
            Assert.Equal("GST", resolved.ResolvedTaxMode);
            Assert.Equal("CountryConfig", resolved.ResolutionSource);

            var get = await service.GetTaxConfiguration(tenant, store);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var dto = Assert.IsType<TaxConfigurationDto>(get.ResultSet);
            Assert.Equal("GST", dto.TaxMode);
            Assert.Equal("CountryConfig", dto.LegacyFallbackSource);
        }

        [Fact]
        public async Task ResolveTaxMode_LegacyIndianGstFallback_WhenTaxModeAndCountryMissing()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            context.TaxInvoiceSettings.Add(new TaxInvoiceSettingsEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableTax = true,
                GstPercent = 18,
                PricesIncludeTax = false,
                InvoicePrefix = "INV",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                TaxMode = null,
                CountryCode = null,
                CurrencyCode = null,
                IsPricesInclusive = false,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var service = _fixture.CreateService(context);
            var resolve = await service.ResolveTaxMode(tenant, store);

            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
            var resolved = Assert.IsType<TaxModeResolutionDto>(resolve.ResultSet);
            Assert.Equal("GST", resolved.ResolvedTaxMode);
            Assert.Equal("LegacyGSTFallback", resolved.ResolutionSource);
        }

        private void EnsureFixtureReady()
        {
            Assert.True(_fixture.IsAvailable, _fixture.UnavailableReason ?? "SQL Server test fixture unavailable.");
        }

        private static (string Tenant, string Store) NewScope()
            => ($"tenant-{Guid.NewGuid():N}", $"store-{Guid.NewGuid():N}");
    }
}
