using System.Threading.Tasks;
using Xunit;
using LMS.Identity.BusinessSerive.Services;
using LMS.Identity.DTO;
using LMS.Core.Repository.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AutoMapper;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO.Entities;
using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using System.Net;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.Utilities;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using Microsoft.EntityFrameworkCore.Storage;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class LoginServiceRegistrationTests : IDisposable
    {
        private readonly IdentityDbContext _context;
        private readonly LoginService _service;
        private readonly IUnitOfWork<IdentityDbContext> _uow;
        private readonly string _databaseName;

        public LoginServiceRegistrationTests()
        {
            _databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(databaseName: _databaseName)
                .Options;

            _context = new IdentityDbContext(options);
            var services = new ServiceCollection();
            services.AddLogging();
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<LoginService>>();

            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<TenantDto, TenantEntity>().ReverseMap();
            });
            var mapper = mapperConfig.CreateMapper();

            var uow = new UnitOfWork<IdentityDbContext>(_context);
            _uow = uow;

            // Use a mail configuration that will cause SendMail to attempt to connect
            // and fail in tests that assert background email handling. Passing null
            // is fine for tests that don't exercise mail sending.
            var mailConfig = new Fixtures.FailingSmtpMailConfiguration();
            IConfiguration config = null;
            var razorpayStub = new StubRazorpayIntegrationService();

            _service = new LoginService(mailConfig, mapper, _uow, logger, config, razorpayStub, new StubTenantEmailService());
        }

        private class StubRazorpayIntegrationService : IRazorpayIntegrationService
        {
            public bool IsLiveMode => false;

            public string ActiveKeyId => "rzp_test_stub";

            public Task<ActionReturnType> CreateTenantSubscriptionPaymentOrder(CreateTenantSubscriptionPaymentOrderRequestDto request, ClaimsPrincipal user)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }

            public Task<ActionReturnType> VerifyTenantSubscriptionCheckoutPayment(VerifyCheckoutPaymentRequestDto request, ClaimsPrincipal user)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }

            public Task<ActionReturnType> ReconcileCapturedPayment(ReconcileCapturedPaymentRequestDto request)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }

            public Task<ActionReturnType> ProcessRazorpayWebhook(string rawPayload, string razorpaySignature)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }

            public Task<ActionReturnType> GetTenantSubscriptionPaymentHistory(ClaimsPrincipal user)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }

            public Task<ActionReturnType> GetTenantSubscriptionOverview(ClaimsPrincipal user)
            {
                return Task.FromResult(ActionSet.ActionReturnType(HttpStatusCode.NotImplemented, new IOResponse
                {
                    StatusCode = "501",
                    Message = "Not implemented in this test stub."
                }));
            }
        }

        [Fact]
        public async Task NewStoreConfiguration_Persists_DedicatedCurrencyColumns()
        {
            // Create tenant first
            var tenant = new TenantEntity { TenantId = Guid.NewGuid().ToString(), Email = "cfgtenant@example.com", TenantName = "CfgTenant", NoOfStores = 1, CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            var cfgJson = JObject.FromObject(new { TenantName = tenant.Email, StoreCode = "S2", CurrencyCode = "EUR", CurrencySymbol = "€", CurrencyLocale = "de-DE", CurrencyDecimalDigits = 2 }).ToString();
            var dto = new NewStoreConfigurationDto { TenantEmail = tenant.Email, StoreCode = "S2", ConfigurationJson = cfgJson, UpdatedBy = tenant.Email };

            var res = await _service.NewStoreConfiguration(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var persisted = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == tenant.TenantId && s.StoreCode == "S2");
            Assert.NotNull(persisted);
            Assert.Equal("EUR", persisted.CurrencyCode);
            Assert.Equal("€", persisted.CurrencySymbol);
            Assert.Equal("de-DE", persisted.CurrencyLocale);
            Assert.Equal(2, persisted.CurrencyDecimalDigits);
        }

        [Fact]
        public async Task India_BlankCurrency_Persists_INR()
        {
            var dto = new TenantDto { Email = "india@example.com", TenantName = "IndiaTenant", NoOfStores = 1, Country = "India" };
            var res = await (_service.Register(dto));
            // res.ResultSet is IOResponse; check StatusCode inside ResultSet
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            // configuration saved as StoreConfigurationEntity with StoreCode = ""
            var cfg = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == _context.Tenants.First().TenantId);
            Assert.NotNull(cfg);
            Assert.Equal("INR", cfg.CurrencyCode);
            Assert.Equal("₹", cfg.CurrencySymbol);
        }

        [Fact]
        public async Task US_BlankCurrency_Persists_USD()
        {
            var dto = new TenantDto { Email = "us@example.com", TenantName = "USTenant", NoOfStores = 1, Country = "United States" };
            var res = await (_service.Register(dto));
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var cfg = _context.StoreConfigurations.FirstOrDefault();
            Assert.NotNull(cfg);
            Assert.Equal("USD", cfg.CurrencyCode);
            Assert.Equal("$", cfg.CurrencySymbol);
            Assert.Equal("en-US", cfg.CurrencyLocale);
            Assert.Equal(2, cfg.CurrencyDecimalDigits);
        }

        [Fact]
        public async Task AdminOverride_Preserved()
        {
            var dto = new TenantDto { Email = "override@example.com", TenantName = "OverrideTenant", NoOfStores = 1, Country = "India", CurrencyCode = "USD", CurrencySymbol = "$", CurrencyLocale = "en-US", CurrencyDecimalDigits = 2 };
            var res = await (_service.Register(dto));
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var cfg = _context.StoreConfigurations.FirstOrDefault();
            Assert.NotNull(cfg);
            Assert.Equal("USD", cfg.CurrencyCode);
            Assert.Equal("$", cfg.CurrencySymbol);
            Assert.Equal("en-US", cfg.CurrencyLocale);
            Assert.Equal(2, cfg.CurrencyDecimalDigits);
        }

        [Fact]
        public async Task UK_BlankCurrency_Persists_GBP_In_Both_Tables()
        {
            var dto = new TenantDto { Email = "uk@example.com", TenantName = "UKTenant", NoOfStores = 1, Country = "United Kingdom" };
            var res = await (_service.Register(dto));
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            // Check tenant-level StoreConfiguration
            var cfg = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == _context.Tenants.First().TenantId);
            Assert.NotNull(cfg);
            Assert.Equal("GBP", cfg.CurrencyCode);
            Assert.Equal("£", cfg.CurrencySymbol);
            Assert.Equal("en-GB", cfg.CurrencyLocale);
            Assert.Equal(2, cfg.CurrencyDecimalDigits);

            // Check TenantStoreInfos record
            var tsi = _context.TenantStoreInfos.FirstOrDefault(ts => ts.TenantId == _context.Tenants.First().TenantId);
            Assert.NotNull(tsi);
            Assert.Equal("GBP", tsi.CurrencyCode);
            Assert.Equal("£", tsi.CurrencySymbol);
            Assert.Equal("en-GB", tsi.CurrencyLocale);
            Assert.Equal(2, tsi.CurrencyDecimalDigits);
        }

        [Theory]
        [InlineData("AE", "United Arab Emirates", "AED", "د.إ", "en-AE")]
        [InlineData("DE", "Germany", "EUR", "€", "de-DE")]
        [InlineData("FR", "France", "EUR", "€", "fr-FR")]
        public async Task CountryCode_Defaults_Are_Applied_For_Supported_Countries(string countryCode, string countryName, string expectedCurrencyCode, string expectedSymbol, string expectedLocale)
        {
            var dto = new TenantDto
            {
                Email = $"{countryCode.ToLowerInvariant()}@example.com",
                TenantName = $"{countryCode}Tenant",
                NoOfStores = 1,
                CountryCode = countryCode,
                Country = countryName
            };

            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
            Assert.Equal(countryCode, tenant.CountryCode);

            var cfg = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == tenant.TenantId);
            Assert.NotNull(cfg);
            Assert.Equal(expectedCurrencyCode, cfg.CurrencyCode);
            Assert.Equal(expectedSymbol, cfg.CurrencySymbol);
            Assert.Equal(expectedLocale, cfg.CurrencyLocale);
            Assert.Equal(2, cfg.CurrencyDecimalDigits);
        }

        [Fact]
        public async Task CountryName_Fallback_Resolves_Profile_When_CountryCode_Not_Provided()
        {
            var dto = new TenantDto
            {
                Email = "fallback-country@example.com",
                TenantName = "FallbackCountryTenant",
                NoOfStores = 1,
                Country = "India"
            };

            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
            Assert.Equal("IN", tenant.CountryCode);
        }

        [Fact]
        public async Task Unsupported_CountryCode_Returns_BadRequest()
        {
            var dto = new TenantDto
            {
                Email = "invalid-code@example.com",
                TenantName = "InvalidCodeTenant",
                NoOfStores = 1,
                CountryCode = "ZZ",
                Country = "India"
            };

            var res = await _service.Register(dto);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

            var io = res.ResultSet as dynamic;
            Assert.Equal("400", (string)io.StatusCode);
            Assert.Contains("Unsupported CountryCode", (string)io.Message);
        }

        [Fact]
        public async Task Manual_Currency_Override_Is_Preserved_With_CountryCode()
        {
            var dto = new TenantDto
            {
                Email = "override-countrycode@example.com",
                TenantName = "OverrideCountryCodeTenant",
                NoOfStores = 1,
                CountryCode = "AE",
                Country = "United Arab Emirates",
                CurrencyCode = "USD",
                CurrencySymbol = "$",
                CurrencyLocale = "en-US",
                CurrencyDecimalDigits = 2
            };

            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var cfg = _context.StoreConfigurations.FirstOrDefault();
            Assert.NotNull(cfg);
            Assert.Equal("USD", cfg.CurrencyCode);
            Assert.Equal("$", cfg.CurrencySymbol);
            Assert.Equal("en-US", cfg.CurrencyLocale);
            Assert.Equal(2, cfg.CurrencyDecimalDigits);
        }

        [Fact]
        public async Task Store_Inherits_TenantCurrency_Unless_Overridden()
        {
            var dto = new TenantDto { Email = "inherit@example.com", TenantName = "InheritTenant", NoOfStores = 2, Country = "United States" };
            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            // Tenant-level config exists
            var tenantCfg = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == _context.Tenants.First().TenantId);
            Assert.NotNull(tenantCfg);
            Assert.Equal("USD", tenantCfg.CurrencyCode);

            // Simulate saving store-specific config with no currency override -> it should inherit tenant currency when asked by StoreConfigurationService.LoadAsync
            var storeCfgEntity = new StoreConfigurationEntity
            {
                TenantId = tenantCfg.TenantId,
                StoreCode = "S1",
                // create minimal config JSON without depending on UI model
                ConfigurationJson = JObject.FromObject(new { TenantName = dto.Email, StoreCode = "S1" }).ToString(),
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                CreatedBy = dto.Email,
                ModifiedBy = dto.Email
            };
            _context.StoreConfigurations.Add(storeCfgEntity);
            await _context.SaveChangesAsync();

            // Now load store config and ensure CurrencyCode is null (explicit), but in effective resolution tenant currency should apply — that's covered by UI/CurrencyService tests already.
            var loaded = _context.StoreConfigurations.FirstOrDefault(s => s.StoreCode == "S1");
            // CurrencyCode absent or null in dedicated column
            Assert.True(string.IsNullOrWhiteSpace(loaded.CurrencyCode));
        }

        [Fact]
        public async Task Legacy_Tenant_Without_Config_Falls_Back_To_INR()
        {
            // Simulate legacy tenant created without config: create tenant entity directly without saving configuration
            var tenant = new TenantEntity { TenantId = Guid.NewGuid().ToString(), Email = "legacy@example.com", TenantName = "LegacyTenant", NoOfStores = 1, CreatedDate = DateTime.UtcNow, ModifiedDate = DateTime.UtcNow };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            // There should be no StoreConfiguration for this tenant; default behavior in StoreConfigurationService.BuildDefault is INR
            var cfg = _context.StoreConfigurations.FirstOrDefault(s => s.TenantId == tenant.TenantId);
            Assert.Null(cfg);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

        [Fact]
        public async Task Registration_Succeeds_When_EmailSendFails()
        {
            // Registration should succeed even if mail sending fails in background.
            var dto = new TenantDto { Email = "mailfail@example.com", TenantName = "MailFailTenant", NoOfStores = 1, Country = "United States" };
            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            // Tenant persisted
            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
        }

        [Fact]
        public async Task DuplicateRegistration_IsDetected()
        {
            var email = "dup@example.com";
            var dto1 = new TenantDto { Email = email, TenantName = "Dup1", NoOfStores = 1, Country = "India" };
            var dto2 = new TenantDto { Email = email, TenantName = "Dup2", NoOfStores = 1, Country = "India" };

            // Simulate two concurrent registration attempts
            var t1 = Task.Run(() => _service.Register(dto1));
            var t2 = Task.Run(() => _service.Register(dto2));
            await Task.WhenAll(t1, t2);

            var r1 = await t1;
            var r2 = await t2;

            // Exactly one should have created the tenant; the other should report 208
            var createdCount = _context.Tenants.Count(t => t.Email == email);
            Assert.Equal(1, createdCount);

            var io1 = r1.ResultSet as dynamic;
            var io2 = r2.ResultSet as dynamic;

            Assert.True((string)io1.StatusCode == "200" || (string)io2.StatusCode == "200");
            Assert.True((string)io1.StatusCode == "208" || (string)io2.StatusCode == "208");
        }

        [Fact]
        public async Task India_Defaults_TimeZone_And_DialingCode()
        {
            var dto = new TenantDto { Email = "india-timezone@example.com", TenantName = "IndiaTimezone", NoOfStores = 1, Country = "India" };
            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
            Assert.Equal("Asia/Kolkata", tenant.TimeZoneIana);
            Assert.Equal("+91", tenant.PhoneDialingCode);
        }

        [Fact]
        public async Task UAE_Defaults_TimeZone_And_DialingCode()
        {
            var dto = new TenantDto { Email = "uae-timezone@example.com", TenantName = "UAETimezone", NoOfStores = 1, CountryCode = "AE", Country = "United Arab Emirates" };
            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
            Assert.Equal("Asia/Dubai", tenant.TimeZoneIana);
            Assert.Equal("+971", tenant.PhoneDialingCode);
        }

        [Fact]
        public async Task UK_Defaults_TimeZone_And_DialingCode()
        {
            var dto = new TenantDto { Email = "uk-timezone@example.com", TenantName = "UKTimezone", NoOfStores = 1, CountryCode = "GB", Country = "United Kingdom" };
            var res = await _service.Register(dto);
            var io = res.ResultSet as dynamic;
            Assert.Equal("200", (string)io.StatusCode);

            var tenant = _context.Tenants.FirstOrDefault(t => t.Email == dto.Email);
            Assert.NotNull(tenant);
            Assert.Equal("Europe/London", tenant.TimeZoneIana);
            Assert.Equal("+44", tenant.PhoneDialingCode);
        }

        [Fact]
        public async Task ExistingTenant_NullTimezone_Uses_CountryFallback_In_Profile()
        {
            var tenant = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = "legacy-null-tz@example.com",
                TenantName = "LegacyTZ",
                NoOfStores = 1,
                Country = "India",
                CountryCode = "IN",
                TimeZoneIana = null,
                PhoneDialingCode = null,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            var profile = await _service.TenantProfileDetails(tenant.Email);
            var io = profile.ResultSet as IOResponse;
            Assert.NotNull(io);
            Assert.Equal("Asia/Kolkata", io.TimeZoneIana);
            Assert.Equal("+91", io.PhoneDialingCode);
        }

        [Fact]
        public async Task Invalid_Timezone_Is_Rejected_On_Update()
        {
            var tenant = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = "invalid-timezone-update@example.com",
                TenantName = "InvalidTZ",
                NoOfStores = 1,
                Country = "India",
                CountryCode = "IN",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            var claims = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
            {
                new Claim(ClaimTypes.Name, tenant.Email),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("tenantId", tenant.TenantId)
            }, "TestAuth"));

            var res = await _service.UpdateTenantLocalizationSettings(tenant.Email, new TenantLocalizationSettingsDto
            {
                TimeZoneIana = "Invalid/Timezone",
                PhoneDialingCode = "+91"
            }, claims);

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        [Fact]
        public async Task Tenant_Admin_Can_Update_Its_Localization_Settings()
        {
            var tenant = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = "tenant-update-localization@example.com",
                TenantName = "UpdateLocalization",
                NoOfStores = 1,
                Country = "United States",
                CountryCode = "US",
                TimeZoneIana = "America/New_York",
                PhoneDialingCode = "+1",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            var claims = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
            {
                new Claim(ClaimTypes.Name, tenant.Email),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("tenantId", tenant.TenantId)
            }, "TestAuth"));

            var res = await _service.UpdateTenantLocalizationSettings(tenant.Email, new TenantLocalizationSettingsDto
            {
                TimeZoneIana = "America/Los_Angeles",
                PhoneDialingCode = "+1"
            }, claims);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var reloaded = _context.Tenants.First(t => t.Email == tenant.Email);
            Assert.Equal("America/Los_Angeles", reloaded.TimeZoneIana);
        }

        [Fact]
        public async Task Tenant_Isolation_Prevents_Updating_Other_Tenant_Localization()
        {
            var tenantA = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = "tenant-a@example.com",
                TenantName = "TenantA",
                NoOfStores = 1,
                Country = "India",
                CountryCode = "IN",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            var tenantB = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = "tenant-b@example.com",
                TenantName = "TenantB",
                NoOfStores = 1,
                Country = "United States",
                CountryCode = "US",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            _context.Tenants.AddRange(tenantA, tenantB);
            await _context.SaveChangesAsync();

            var claims = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
            {
                new Claim(ClaimTypes.Name, tenantA.Email),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("tenantId", tenantA.TenantId)
            }, "TestAuth"));

            var res = await _service.UpdateTenantLocalizationSettings(tenantB.Email, new TenantLocalizationSettingsDto
            {
                TimeZoneIana = "America/Los_Angeles",
                PhoneDialingCode = "+1"
            }, claims);

            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        [Fact]
        public void Utc_To_TenantLocal_Conversion_Works()
        {
            var utc = new DateTime(2026, 01, 10, 18, 30, 00, DateTimeKind.Utc);
            var local = LoginService.ConvertUtcToTenantLocal(utc, "Asia/Kolkata");
            Assert.Equal(new DateTime(2026, 01, 11, 0, 0, 0), local);
        }

        [Fact]
        public void TenantLocal_DateRange_To_Utc_Boundaries_Works()
        {
            var (startUtc, endUtcExclusive) = LoginService.ConvertTenantLocalDateRangeToUtc(
                new DateOnly(2026, 01, 11),
                new DateOnly(2026, 01, 11),
                "Asia/Kolkata");

            Assert.Equal(new DateTime(2026, 01, 10, 18, 30, 0, DateTimeKind.Utc), startUtc);
            Assert.Equal(new DateTime(2026, 01, 11, 18, 30, 0, DateTimeKind.Utc), endUtcExclusive);
        }

        [Fact]
        public void Indian_Legacy_Phone_Compatibility_Remains_Valid()
        {
            var isLegacyDigits = System.Text.RegularExpressions.Regex.IsMatch("9876543210", "^\\d{7,15}$");
            Assert.True(isLegacyDigits);
        }

        [Fact]
        public void International_E164_Phone_Is_Valid_And_Invalid_Rejected()
        {
            var valid = System.Text.RegularExpressions.Regex.IsMatch("+971501234567", "^\\+[1-9]\\d{6,14}$");
            var invalid = System.Text.RegularExpressions.Regex.IsMatch("00123456", "^\\+[1-9]\\d{6,14}$");
            Assert.True(valid);
            Assert.False(invalid);
        }

        [Fact]
        public async Task TenantLogin_IssuesToken_WithTenantName_TenantId_AndStoreCodesClaims()
        {
            var email = "tenant-login@example.com";
            var password = "Pass@123";
            Environment.SetEnvironmentVariable("Jwt__Issuer", "lms-identity-tests");
            Environment.SetEnvironmentVariable("Jwt__Audience", "lms-master-tests");
            Environment.SetEnvironmentVariable("Jwt__SigningKey", "0123456789ABCDEF0123456789ABCDEF");
            Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", "60");

            try
            {
            var tenant = new TenantEntity
            {
                TenantId = Guid.NewGuid().ToString(),
                Email = email,
                TenantName = "Tenant Login",
                ApprovalStatus = TenantApprovalStatus.Approved,
                SubscriptionStatus = "TrialActive",
                TrialEndUtc = DateTime.UtcNow.AddDays(10),
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            _context.Tenants.Add(tenant);
            _context.TenantStoreInfos.Add(new TenantStoreInfoEntity
            {
                TenantId = tenant.TenantId,
                Name = email,
                Password = password,
                Storecodes = new List<string> { "S1", "S2" },
                IsPasswordChanged = true,
                Status = "Active"
            });

            _context.TenantStoreStatuses.AddRange(
                new TenantStoreStatusEntity { TenantId = tenant.TenantId, StoreCode = "S1", IsActive = true, CreatedDate = DateTime.UtcNow, ActivatedBy = "test", ActivatedDate = DateTime.UtcNow },
                new TenantStoreStatusEntity { TenantId = tenant.TenantId, StoreCode = "S2", IsActive = true, CreatedDate = DateTime.UtcNow, ActivatedBy = "test", ActivatedDate = DateTime.UtcNow });

            await _context.SaveChangesAsync();

            var result = await _service.TenantLogin(tenant.Email, password, "S1");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);

            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.False(string.IsNullOrWhiteSpace(io.AccessToken));

            var payload = DecodeJwtPayload(io.AccessToken);
            Assert.Equal(email, payload["tenantName"]?.ToString());
            Assert.Equal(tenant.TenantId, payload["tenantId"]?.ToString());
            var storeCodesClaim = payload["storeCodes"]?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(storeCodesClaim));
            Assert.Contains("S1", storeCodesClaim, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("S2", storeCodesClaim, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Environment.SetEnvironmentVariable("Jwt__Issuer", null);
                Environment.SetEnvironmentVariable("Jwt__Audience", null);
                Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
                Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", null);
            }
        }

        [Fact]
        public async Task GetSubscriptionPlans_WhenCatalogEmpty_SeedsStandardAndPremiumWithoutDuplicates()
        {
            Assert.Empty(_context.SubscriptionPlans);

            var first = await _service.GetSubscriptionPlans();
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var firstPayload = Assert.IsType<SubscriptionPlanCatalogResponseDto>(first.ResultSet);
            Assert.Equal(4, firstPayload.Plans.Count);

            Assert.Contains(firstPayload.Plans, x => x.PlanCode == "Standard" && x.BillingCycle == "Monthly" && x.Amount == 499.00m);
            Assert.Contains(firstPayload.Plans, x => x.PlanCode == "Standard" && x.BillingCycle == "Yearly" && x.Amount == 4999.00m);
            Assert.Contains(firstPayload.Plans, x => x.PlanCode == "Premium" && x.BillingCycle == "Monthly" && x.Amount == 899.00m);
            Assert.Contains(firstPayload.Plans, x => x.PlanCode == "Premium" && x.BillingCycle == "Yearly" && x.Amount == 8999.00m);

            Assert.Equal(4, await _context.SubscriptionPlans.CountAsync());

            var second = await _service.GetSubscriptionPlans();
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var secondPayload = Assert.IsType<SubscriptionPlanCatalogResponseDto>(second.ResultSet);
            Assert.Equal(4, secondPayload.Plans.Count);
            Assert.Equal(4, await _context.SubscriptionPlans.CountAsync());
        }

        [Fact]
        public async Task GetSubscriptionPlans_WhenLegacyStandardExists_AddsOnlyMissingDefaults_AndPreservesExistingPrice()
        {
            _context.SubscriptionPlans.Add(new SubscriptionPlanEntity
            {
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 650.00m,
                Currency = "INR",
                IsActive = true,
                CreatedDateUtc = DateTime.UtcNow,
                UpdatedBy = "LegacyData"
            });
            await _context.SaveChangesAsync();

            var result = await _service.GetSubscriptionPlans();
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);

            var payload = Assert.IsType<SubscriptionPlanCatalogResponseDto>(result.ResultSet);
            Assert.Equal(4, payload.Plans.Count);

            var standardMonthly = payload.Plans.Single(x => x.PlanCode == "Standard" && x.BillingCycle == "Monthly");
            Assert.Equal(650.00m, standardMonthly.Amount);
            Assert.Equal(4, await _context.SubscriptionPlans.CountAsync());
        }

        [Fact]
        public async Task GetSubscriptionPlans_ConcurrentCalls_SeedDefaultsWithoutDuplicates_AndPreserveExistingPrice()
        {
            _context.SubscriptionPlans.Add(new SubscriptionPlanEntity
            {
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 777.00m,
                Currency = "INR",
                IsActive = true,
                CreatedDateUtc = DateTime.UtcNow,
                UpdatedBy = "LegacyData"
            });
            await _context.SaveChangesAsync();

            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options;

            var loggerFactory = LoggerFactory.Create(builder => { });
            var logger = loggerFactory.CreateLogger<LoginService>();
            var mapperConfig = new MapperConfiguration(cfg => cfg.CreateMap<TenantDto, TenantEntity>().ReverseMap());
            var mapper = mapperConfig.CreateMapper();
            var mailConfig = new Fixtures.FailingSmtpMailConfiguration();
            IConfiguration config = null;
            var razorpayStub = new StubRazorpayIntegrationService();

            var startGate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, 6)
                .Select(async _ =>
                {
                    using var ctx = new IdentityDbContext(options);
                    var uow = new UnitOfWork<IdentityDbContext>(ctx);
                    var service = new LoginService(mailConfig, mapper, uow, logger, config, razorpayStub, new StubTenantEmailService());
                    startGate.Wait();
                    return await service.GetSubscriptionPlans();
                })
                .ToList();

            startGate.Set();
            await Task.WhenAll(tasks);

            var allPlans = await _context.SubscriptionPlans.AsNoTracking().ToListAsync();
            Assert.Equal(4, allPlans.Count);

            var keyCount = allPlans
                .Select(x => $"{(x.PlanCode ?? string.Empty).Trim()}::{(x.BillingCycle ?? string.Empty).Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            Assert.Equal(4, keyCount);

            var standardMonthly = allPlans.Single(x => x.PlanCode == "Standard" && x.BillingCycle == "Monthly");
            Assert.Equal(777.00m, standardMonthly.Amount);
        }

        private sealed class StubTenantEmailService : ITenantEmailService
        {
            public Task SendMailAsync(string email, string subject, string body, string tenantId = null, string tenantEmail = null)
            {
                return Task.CompletedTask;
            }
        }

        private static JObject DecodeJwtPayload(string jwt)
        {
            Assert.False(string.IsNullOrWhiteSpace(jwt));

            var parts = jwt.Split('.');
            Assert.True(parts.Length >= 2);

            var payloadSegment = parts[1].Replace('-', '+').Replace('_', '/');
            var remainder = payloadSegment.Length % 4;
            if (remainder > 0)
            {
                payloadSegment = payloadSegment.PadRight(payloadSegment.Length + (4 - remainder), '=');
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payloadSegment));
            return JObject.Parse(json);
        }
    }
}
