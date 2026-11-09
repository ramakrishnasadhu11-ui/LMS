using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using LMS.Master.Utilities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerServiceCreateLaundryOrderTaxIntegrationTests : IClassFixture<RazorpaySqlServerFixture>
    {
        private readonly RazorpaySqlServerFixture _fixture;

        public CustomerServiceCreateLaundryOrderTaxIntegrationTests(RazorpaySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CreateLaundryOrder_GstLegacyCompatibility_PopulatesCgstSgst_AndPersistsSnapshots()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-GST", "GST Customer");
            await SeedTaxSettingsAsync(context, tenant, store, "GST", "IN", "INR", enableTax: true, gstPercent: 18m, pricesIncludeTax: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-GST",
                CustomerName = "GST Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                TaxAmount = 999m,
                CgstAmount = 999m,
                SgstAmount = 999m,
                TotalAmount = 1m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(100m, dto.OrderAmount);
            Assert.Equal(100m, dto.SubTotal);
            Assert.Equal(18m, dto.TaxAmount);
            Assert.Equal(9m, dto.CgstAmount);
            Assert.Equal(9m, dto.SgstAmount);
            Assert.Equal(118m, dto.TotalAmount);

            var taxLines = await context.LaundryOrderTaxLines
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .OrderBy(x => x.LineNo)
                .ToListAsync();

            Assert.Equal(2, taxLines.Count);
            Assert.Equal("GST", taxLines[0].TaxMode);
            Assert.Equal("INR", taxLines[0].CurrencyCode);
            Assert.Equal("CGST", taxLines[0].ComponentCode);
            Assert.Equal(9m, taxLines[0].TaxAmount);
            Assert.Equal("SGST", taxLines[1].ComponentCode);
            Assert.Equal(9m, taxLines[1].TaxAmount);
        }

        [Fact]
        public async Task CreateLaundryOrder_VatExclusive_ComputesServerTax_AndPersistsSnapshot()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-VAT-EX", "VAT Exclusive Customer");
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "VAT", "GB", "GBP", enableTax: true, gstPercent: 0m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "VAT", "VAT", 18m, 1, isCompound: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-VAT-EX",
                CustomerName = "VAT Exclusive Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                TaxAmount = 0m,
                TotalAmount = 0m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(100m, dto.SubTotal);
            Assert.Equal(18m, dto.TaxAmount);
            Assert.Equal(118m, dto.TotalAmount);
            Assert.Equal(0m, dto.CgstAmount);
            Assert.Equal(0m, dto.SgstAmount);

            var taxLines = await context.LaundryOrderTaxLines
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .ToListAsync();

            Assert.Single(taxLines);
            Assert.Equal("VAT", taxLines[0].ComponentCode);
            Assert.Equal(100m, taxLines[0].TaxableBaseAmount);
            Assert.Equal(18m, taxLines[0].TaxAmount);
        }

        [Fact]
        public async Task CreateLaundryOrder_VatInclusive_ComputesServerTax_AndPersistsSnapshot()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-VAT-IN", "VAT Inclusive Customer");
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "VAT", "GB", "GBP", enableTax: true, gstPercent: 0m, pricesIncludeTax: true);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "VAT", "VAT", 18m, 1, isCompound: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-VAT-IN",
                CustomerName = "VAT Inclusive Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 59m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(118m, dto.OrderAmount);
            Assert.Equal(100m, dto.SubTotal);
            Assert.Equal(18m, dto.TaxAmount);
            Assert.Equal(118m, dto.TotalAmount);

            var taxLine = await context.LaundryOrderTaxLines
                .SingleAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);
            Assert.Equal("GBP", taxLine.CurrencyCode);
            Assert.Equal(100m, taxLine.TaxableBaseAmount);
            Assert.Equal(18m, taxLine.TaxAmount);
        }

        [Fact]
        public async Task CreateLaundryOrder_SalesTaxCompound_ComputesCompoundTax_AndPersistsSnapshots()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-SALES", "SalesTax Customer");
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "SalesTax", "US", "USD", enableTax: true, gstPercent: 0m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "BASE", "Base", 10m, 1, isCompound: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "COMP", "Compound", 10m, 2, isCompound: true);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-SALES",
                CustomerName = "SalesTax Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(100m, dto.SubTotal);
            Assert.Equal(21m, dto.TaxAmount);
            Assert.Equal(121m, dto.TotalAmount);

            var taxLines = await context.LaundryOrderTaxLines
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .OrderBy(x => x.LineNo)
                .ToListAsync();

            Assert.Equal(2, taxLines.Count);
            Assert.Equal("BASE", taxLines[0].ComponentCode);
            Assert.Equal(100m, taxLines[0].TaxableBaseAmount);
            Assert.Equal(10m, taxLines[0].TaxAmount);
            Assert.Equal("COMP", taxLines[1].ComponentCode);
            Assert.Equal(110m, taxLines[1].TaxableBaseAmount);
            Assert.Equal(11m, taxLines[1].TaxAmount);
        }

        [Fact]
        public async Task CreateLaundryOrder_NoTax_ProducesZeroTax_AndNoSnapshots()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-NOTAX", "NoTax Customer");
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-NOTAX",
                CustomerName = "NoTax Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                TaxAmount = 1000m,
                TotalAmount = 1m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(100m, dto.SubTotal);
            Assert.Equal(0m, dto.TaxAmount);
            Assert.Equal(100m, dto.TotalAmount);
            Assert.Equal(0m, dto.CgstAmount);
            Assert.Equal(0m, dto.SgstAmount);

            var taxLinesCount = await context.LaundryOrderTaxLines
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);
            Assert.Equal(0, taxLinesCount);
        }

        [Fact]
        public async Task CreateLaundryOrder_GstConfiguredButDisabled_DoesNotChargeTax_AndLeavesPriorInvoiceSnapshotUnchanged()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-GST-DIS", "GST Disabled Customer");

            var settings = await SeedTaxSettingsAsync(context, tenant, store, "GST", "IN", "INR", enableTax: true, gstPercent: 18m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "CGST", "Central GST", 9m, 1, isCompound: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "SGST", "State GST", 9m, 2, isCompound: false);

            var service = _fixture.CreateService(context);

            var firstOrderResult = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-GST-DIS",
                CustomerName = "GST Disabled Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, firstOrderResult.StatusCode);
            var firstOrder = Assert.IsType<LaundryOrderDto>(firstOrderResult.ResultSet);
            Assert.Equal(18m, firstOrder.TaxAmount);

            var firstOrderTaxLinesBefore = await context.LaundryOrderTaxLines
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == firstOrder.OrderNo)
                .OrderBy(x => x.LineNo)
                .Select(x => new { x.ComponentCode, x.RatePercent, x.TaxAmount })
                .ToListAsync();
            Assert.Equal(2, firstOrderTaxLinesBefore.Count);

            var disableResult = await service.SaveTaxInvoiceSettings(new TaxInvoiceSettingsDto
            {
                TenantName = tenant,
                StoreCode = store,
                EnableTax = false,
                GstPercent = 18m,
                PricesIncludeTax = false,
                InvoicePrefix = settings.InvoicePrefix,
                NextInvoiceNumber = settings.NextInvoiceNumber,
                InvoiceNumberPadding = settings.InvoiceNumberPadding,
                ResetInvoiceNumberYearly = settings.ResetInvoiceNumberYearly,
                Notes = settings.Notes
            });

            Assert.Equal(HttpStatusCode.OK, disableResult.StatusCode);

            var secondOrderResult = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-GST-DIS",
                CustomerName = "GST Disabled Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, secondOrderResult.StatusCode);
            var secondOrder = Assert.IsType<LaundryOrderDto>(secondOrderResult.ResultSet);
            Assert.Equal(100m, secondOrder.SubTotal);
            Assert.Equal(0m, secondOrder.TaxAmount);
            Assert.Equal(100m, secondOrder.TotalAmount);
            Assert.Equal(0m, secondOrder.CgstAmount);
            Assert.Equal(0m, secondOrder.SgstAmount);

            var secondOrderTaxLinesCount = await context.LaundryOrderTaxLines
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == secondOrder.OrderNo);
            Assert.Equal(0, secondOrderTaxLinesCount);

            var firstOrderTaxLinesAfter = await context.LaundryOrderTaxLines
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == firstOrder.OrderNo)
                .OrderBy(x => x.LineNo)
                .Select(x => new { x.ComponentCode, x.RatePercent, x.TaxAmount })
                .ToListAsync();

            Assert.Equal(firstOrderTaxLinesBefore.Count, firstOrderTaxLinesAfter.Count);
            for (var i = 0; i < firstOrderTaxLinesBefore.Count; i++)
            {
                Assert.Equal(firstOrderTaxLinesBefore[i].ComponentCode, firstOrderTaxLinesAfter[i].ComponentCode);
                Assert.Equal(firstOrderTaxLinesBefore[i].RatePercent, firstOrderTaxLinesAfter[i].RatePercent);
                Assert.Equal(firstOrderTaxLinesBefore[i].TaxAmount, firstOrderTaxLinesAfter[i].TaxAmount);
            }
        }

        [Fact]
        public async Task CreateLaundryOrder_WhenCreationFails_DoesNotPersistOrderOrTaxSnapshotRows()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, "CUST-ROLLBACK", "Rollback Customer");
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "VAT", "GB", "GBP", enableTax: true, gstPercent: 0m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "VAT", "VAT", 18m, 1, isCompound: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = "CUST-ROLLBACK",
                CustomerName = "Rollback Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 100m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("No active laundry item pricing configured for this store. Please configure pricing in Admin Setup.", response.Message);

            await using var verifyContext = _fixture.CreateDbContext();
            var ordersCount = await verifyContext.LaundryOrders
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.CustCode == "CUST-ROLLBACK");
            var taxLinesCount = await verifyContext.LaundryOrderTaxLines
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);

            Assert.Equal(0, ordersCount);
            Assert.Equal(0, taxLinesCount);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesWithoutSavedBarcodeTagSettings_AutoCreatesTags_AndCommitsOrderAndItems()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PIECE-NO-TAGCFG";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Pieces Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 50m);

            var existingTagSettingsCount = await context.BarcodeTagSettings
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);
            Assert.Equal(0, existingTagSettingsCount);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Pieces Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 100m,
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        UnitPrice = 50m,
                        Quantity = 2
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(2, dto.Items.Count);
            Assert.All(dto.Items, x => Assert.False(string.IsNullOrWhiteSpace(x.TagNo)));

            await using var verifyContext = _fixture.CreateDbContext();
            var persistedOrder = await verifyContext.LaundryOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);
            Assert.NotNull(persistedOrder);

            var persistedItems = await verifyContext.LaundryOrderItems
                .AsNoTracking()
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .ToListAsync();
            Assert.Equal(2, persistedItems.Count);
            Assert.All(persistedItems, x => Assert.False(string.IsNullOrWhiteSpace(x.TagNo)));

            var persistedTagSettings = await verifyContext.BarcodeTagSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenant && x.StoreCode == store);
            Assert.NotNull(persistedTagSettings);
            Assert.True(persistedTagSettings.EnableTagging);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesWhenTaggingDisabled_ReturnsBadRequest_AndRollsBackOrderAndItems()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PIECE-TAG-OFF";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Tag Disabled Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Trouser", 60m);

            context.BarcodeTagSettings.Add(new BarcodeTagSettingsEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableTagging = false,
                TagPrefix = "TAG-",
                NextTagNumber = 1,
                TagNumberPadding = 4,
                ResetTagNumberYearly = false,
                Notes = null,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Tag Disabled Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 60m,
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Trouser",
                        UnitPrice = 60m,
                        Quantity = 1
                    }
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("Tagging is disabled for this store. Enable barcode/tag settings before creating piece-based orders.", response.Message);

            await using var verifyContext = _fixture.CreateDbContext();
            var ordersCount = await verifyContext.LaundryOrders
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.CustCode == custCode);
            var itemsCount = await verifyContext.LaundryOrderItems
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);

            Assert.Equal(0, ordersCount);
            Assert.Equal(0, itemsCount);
        }

        [Fact]
        public async Task CreateLaundryOrder_WhenFailureOccursAfterOrderSaveAndDuringTaxSnapshotSave_RollsBackOrderAndTaxRows()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TX-ROLLBACK";

            var failureInterceptor = new CreateLaundryOrderRollbackFailureInterceptor(tenant, store, custCode);
            await using var context = _fixture.CreateDbContext(failureInterceptor);
            await SeedCustomerAsync(context, tenant, store, custCode, "Tx Rollback Customer");
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "VAT", "GB", "GBP", enableTax: true, gstPercent: 0m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "VAT", "VAT", 18m, 1, isCompound: false);

            var service = _fixture.CreateService(context);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Tx Rollback Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaymentMode = "Cash"
            }));

            Assert.Contains("TEST_INJECTED_CREATE_ORDER_AFTER_ORDER_SAVE_BEFORE_TX_COMMIT", ex.Message, StringComparison.Ordinal);

            await using var verifyContext = _fixture.CreateDbContext();
            var ordersCount = await verifyContext.LaundryOrders
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.CustCode == custCode);
            var taxLinesCount = await verifyContext.LaundryOrderTaxLines
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);

            Assert.Equal(0, ordersCount);
            Assert.Equal(0, taxLinesCount);
        }

        private static async Task SeedCustomerAsync(MasterDbContext context, string tenant, string store, string custCode, string customerName)
        {
            context.Customers.Add(new CustomerEntity
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = customerName,
                Address = "Test Address",
                PhoneNumber = "9876543210",
                Email = "test@example.com",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        private static async Task SeedLaundryItemPriceAsync(
            MasterDbContext context,
            string tenant,
            string store,
            string serviceType,
            string category,
            string itemName,
            decimal unitPrice)
        {
            context.LaundryItemPrices.Add(new LaundryItemPriceEntity
            {
                TenantName = tenant,
                StoreCode = store,
                ServiceType = serviceType,
                Category = category,
                ItemName = itemName,
                UnitPrice = unitPrice,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }

        private static async Task<TaxInvoiceSettingsEntity> SeedTaxSettingsAsync(
            MasterDbContext context,
            string tenant,
            string store,
            string taxMode,
            string countryCode,
            string currencyCode,
            bool enableTax,
            decimal gstPercent,
            bool pricesIncludeTax)
        {
            var settings = new TaxInvoiceSettingsEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableTax = enableTax,
                GstPercent = gstPercent,
                PricesIncludeTax = pricesIncludeTax,
                IsPricesInclusive = pricesIncludeTax,
                TaxMode = taxMode,
                CountryCode = countryCode,
                CurrencyCode = currencyCode,
                InvoicePrefix = "INV",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            context.TaxInvoiceSettings.Add(settings);
            await context.SaveChangesAsync();
            return settings;
        }

        private static async Task SeedTaxComponentAsync(
            MasterDbContext context,
            int settingsId,
            string tenant,
            string store,
            string code,
            string displayName,
            decimal ratePercent,
            int priority,
            bool isCompound)
        {
            context.TaxInvoiceComponents.Add(new TaxInvoiceComponentEntity
            {
                TaxInvoiceSettingsId = settingsId,
                TenantName = tenant,
                StoreCode = store,
                Code = code,
                DisplayName = displayName,
                RatePercent = ratePercent,
                Jurisdiction = null,
                Priority = priority,
                IsCompound = isCompound,
                IsActive = true,
                EffectiveFromUtc = null,
                EffectiveToUtc = null,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        private void EnsureFixtureReady()
        {
            Assert.True(_fixture.IsAvailable, _fixture.UnavailableReason ?? "SQL Server test fixture unavailable.");
        }

        private static (string Tenant, string Store) NewScope()
            => ($"tenant-{Guid.NewGuid():N}", $"store-{Guid.NewGuid():N}");
    }
}
