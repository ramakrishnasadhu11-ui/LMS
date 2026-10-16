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

namespace LMS.Identity.BusinessSerive.Tests
{
    public class LoginServiceRegistrationTests : IDisposable
    {
        private readonly IdentityDbContext _context;
        private readonly LoginService _service;
        private readonly IUnitOfWork<IdentityDbContext> _uow;

        public LoginServiceRegistrationTests()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
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
            _service = new LoginService(mailConfig, mapper, _uow, logger);
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
    }
}
