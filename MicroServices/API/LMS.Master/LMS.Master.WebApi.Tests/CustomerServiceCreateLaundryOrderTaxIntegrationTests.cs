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
        public async Task CreateLaundryOrder_WhenPaidNowIsZero_AndAllPaymentModesAreDisabled_AllowsOrderCreation()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PAYMENT-ZERO";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Zero Payment Customer");
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);
            await SeedPaymentSettingsAsync(context, tenant, store, enableCash: false, enableUpi: false, enableCard: false, enableWallet: false, defaultPaymentMode: "Cash");

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Zero Payment Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaidNow = 0m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(0m, dto.PaidNow);
            Assert.True(dto.PendingAmount > 0m);

            await using var verifyContext = _fixture.CreateDbContext();
            var persisted = await verifyContext.LaundryOrders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);
            Assert.NotNull(persisted);
            Assert.Equal(0m, persisted.PaidNow);
        }

        [Fact]
        public async Task CreateLaundryOrder_WhenFullPaymentCollected_AndPaymentModeEnabled_Succeeds()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PAYMENT-FULL";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Full Payment Customer");
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);
            await SeedPaymentSettingsAsync(context, tenant, store, enableCash: false, enableUpi: true, enableCard: false, enableWallet: false, defaultPaymentMode: "UPI");

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Full Payment Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaidNow = 100m,
                PaymentMode = "UPI"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(100m, dto.PaidNow);
            Assert.Equal(0m, dto.PendingAmount);
            Assert.Equal("UPI", dto.PaymentMode);
        }

        [Fact]
        public async Task CreateLaundryOrder_WhenPositivePaymentCollected_AndAllPaymentModesDisabled_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PAYMENT-DISABLED";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Disabled Payment Customer");
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);
            await SeedPaymentSettingsAsync(context, tenant, store, enableCash: false, enableUpi: false, enableCard: false, enableWallet: false, defaultPaymentMode: "Cash");

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Disabled Payment Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaidNow = 25m,
                PaymentMode = "Cash"
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("No payment mode is configured for this store. Save Payment Settings before collecting payment.", response.Message);
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
        public async Task CreateLaundryOrder_Pieces_WithApplicableTopUps_ComputesAndPersistsTopUpSnapshots()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-PIECES";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Pieces Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 50m);
            await SeedTopUpServiceAsync(
                context,
                tenant,
                store,
                "Stain Removal",
                "PerGarment",
                10m,
                new TopUpServiceApplicabilityEntity
                {
                    ServiceType = "Dry Clean",
                    Category = "Men",
                    ItemName = "Shirt",
                    IsActive = true
                });
            await SeedTopUpServiceAsync(
                context,
                tenant,
                store,
                "Perfume",
                "PerOrder",
                15m,
                new TopUpServiceApplicabilityEntity
                {
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    IsActive = true
                });
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Pieces Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        Quantity = 2,
                        UnitPrice = 1m
                    }
                },
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto { TopUpName = "Stain Removal", ChargeType = "PerGarment", UnitPriceSnapshot = 1m },
                    new LaundryOrderTopUpDto { TopUpName = "Perfume", ChargeType = "PerOrder", UnitPriceSnapshot = 0m }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(135m, dto.OrderAmount);
            Assert.Equal(35m, dto.TopUpAmount);
            Assert.Equal(135m, dto.SubTotal);
            Assert.Equal(0m, dto.TaxAmount);
            Assert.Equal(135m, dto.TotalAmount);
            Assert.Equal(2, dto.TopUps.Count);

            var stain = Assert.Single(dto.TopUps.Where(x => x.TopUpName == "Stain Removal"));
            Assert.Equal("PerGarment", stain.ChargeType);
            Assert.Equal(2m, stain.AppliedQuantity);
            Assert.Equal(10m, stain.UnitPriceSnapshot);
            Assert.Equal(20m, stain.LineAmountSnapshot);

            var perfume = Assert.Single(dto.TopUps.Where(x => x.TopUpName == "Perfume"));
            Assert.Equal("PerOrder", perfume.ChargeType);
            Assert.Equal(1m, perfume.AppliedQuantity);
            Assert.Equal(15m, perfume.UnitPriceSnapshot);
            Assert.Equal(15m, perfume.LineAmountSnapshot);

            await using var verifyContext = _fixture.CreateDbContext();
            var persistedTopUps = await verifyContext.LaundryOrderTopUps
                .AsNoTracking()
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .OrderBy(x => x.LineNo)
                .ToListAsync();

            Assert.Equal(2, persistedTopUps.Count);
            Assert.Equal("Stain Removal", persistedTopUps[0].TopUpName);
            Assert.Equal(20m, persistedTopUps[0].LineAmountSnapshot);
            Assert.Equal("Perfume", persistedTopUps[1].TopUpName);
            Assert.Equal(15m, persistedTopUps[1].LineAmountSnapshot);
        }

        [Fact]
        public async Task CreateLaundryOrder_Weight_WithPerGarmentTopUp_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-WEIGHT";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Weight Customer");
            await SeedTopUpServiceAsync(
                context,
                tenant,
                store,
                "Stain Removal",
                "PerGarment",
                10m,
                new TopUpServiceApplicabilityEntity
                {
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    IsActive = true
                });
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Weight Customer",
                ServiceType = "Dry Clean",
                OrderMode = "weight",
                WeightInKg = 2m,
                RatePerKg = 50m,
                PaymentMode = "Cash",
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto { TopUpName = "Stain Removal", ChargeType = "PerGarment" }
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("Per-garment top-up \"Stain Removal\" is not allowed for weight-based orders.", response.Message);
        }

        [Fact]
        public async Task CreateLaundryOrder_WithDuplicateTopUps_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-DUP";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Duplicate Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 50m);
            await SeedTopUpServiceAsync(
                context,
                tenant,
                store,
                "Perfume",
                "PerOrder",
                15m,
                new TopUpServiceApplicabilityEntity
                {
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    IsActive = true
                });

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Duplicate Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        Quantity = 1
                    }
                },
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto { TopUpName = "Perfume" },
                    new LaundryOrderTopUpDto { TopUpName = " perfume " }
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("Duplicate top-up selections are not allowed in an order.", response.Message);
        }

        [Fact]
        public async Task CreateLaundryOrder_WithUnknownTopUp_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-UNKNOWN";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Unknown Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 50m);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Unknown Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        Quantity = 1
                    }
                },
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto { TopUpName = "Not Available" }
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(result.ResultSet);
            Assert.Equal("One or more selected top-up services are unavailable for this store.", response.Message);
        }

        [Fact]
        public async Task CreateLaundryOrder_WhenFailureOccursAfterOrderSaveAndDuringTopUpSnapshotSave_RollsBackOrderAndTopUps()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-TX-ROLLBACK";

            var failureInterceptor = new CreateLaundryOrderRollbackFailureInterceptor(tenant, store, custCode);
            await using var context = _fixture.CreateDbContext(failureInterceptor);
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Rollback Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 50m);
            await SeedTopUpServiceAsync(
                context,
                tenant,
                store,
                "Perfume",
                "PerOrder",
                15m,
                new TopUpServiceApplicabilityEntity
                {
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    IsActive = true
                });
            var settings = await SeedTaxSettingsAsync(context, tenant, store, "VAT", "GB", "GBP", enableTax: true, gstPercent: 0m, pricesIncludeTax: false);
            await SeedTaxComponentAsync(context, settings.Id, tenant, store, "VAT", "VAT", 18m, 1, isCompound: false);

            var service = _fixture.CreateService(context);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Rollback Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        Quantity = 1
                    }
                },
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto { TopUpName = "Perfume" }
                }
            }));

            Assert.Contains("TEST_INJECTED_CREATE_ORDER_AFTER_ORDER_SAVE_BEFORE_TX_COMMIT", ex.Message, StringComparison.Ordinal);

            await using var verifyContext = _fixture.CreateDbContext();
            var ordersCount = await verifyContext.LaundryOrders
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store && x.CustCode == custCode);
            var topUpsCount = await verifyContext.LaundryOrderTopUps
                .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);

            Assert.Equal(0, ordersCount);
            Assert.Equal(0, topUpsCount);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesQuantityOne_UsesGarmentDetails_AndPersistsPieceNo()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PIECE-ONE-DETAIL";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Single Piece Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Shirt", 40m);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Single Piece Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        Quantity = 1,
                        BrandName = "Arrow",
                        PrimaryColor = "White",
                        Pattern = "Striped",
                        ExistingDefects = "Loose button",
                        DefectRemarks = "Front button",
                        SpecialInstructions = "Use mild detergent"
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            var piece = Assert.Single(dto.Items);
            Assert.Equal(1, piece.PieceNo);
            Assert.Equal("Arrow", piece.BrandName);
            Assert.Equal("White", piece.PrimaryColor);
            Assert.Equal("Striped", piece.Pattern);

            await using var verifyContext = _fixture.CreateDbContext();
            var persisted = await verifyContext.LaundryOrderItems
                .AsNoTracking()
                .SingleAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);

            Assert.Equal(1, persisted.PieceNo);
            Assert.Equal("Arrow", persisted.BrandName);
            Assert.Equal("White", persisted.PrimaryColor);
            Assert.Equal("Striped", persisted.Pattern);
            Assert.Equal("Loose button", persisted.ExistingDefects);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesQuantityThree_PersistsDistinctGarmentDetailsPerPiece()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PIECE-THREE-DETAILS";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Three Piece Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Women", "Dress", 55m);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Three Piece Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Women",
                        ItemName = "Dress",
                        Quantity = 3,
                        PieceDetails = new List<LaundryGarmentPieceDetailDto>
                        {
                            new LaundryGarmentPieceDetailDto { PieceNo = 1, BrandName = "B1", PrimaryColor = "Red", Pattern = "Solid" },
                            new LaundryGarmentPieceDetailDto { PieceNo = 2, BrandName = "B2", PrimaryColor = "Blue", Pattern = "Checked" },
                            new LaundryGarmentPieceDetailDto { PieceNo = 3, BrandName = "B3", PrimaryColor = "Green", Pattern = "Printed" }
                        }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(3, dto.Items.Count);

            var dtoPieces = dto.Items.OrderBy(x => x.PieceNo).ToList();
            Assert.Equal("B1", dtoPieces[0].BrandName);
            Assert.Equal("B2", dtoPieces[1].BrandName);
            Assert.Equal("B3", dtoPieces[2].BrandName);

            await using var verifyContext = _fixture.CreateDbContext();
            var persistedItems = await verifyContext.LaundryOrderItems
                .AsNoTracking()
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .OrderBy(x => x.PieceNo)
                .ToListAsync();

            Assert.Equal(3, persistedItems.Count);
            Assert.Equal(1, persistedItems[0].PieceNo);
            Assert.Equal(2, persistedItems[1].PieceNo);
            Assert.Equal(3, persistedItems[2].PieceNo);
            Assert.Equal("B1", persistedItems[0].BrandName);
            Assert.Equal("B2", persistedItems[1].BrandName);
            Assert.Equal("B3", persistedItems[2].BrandName);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesNormalizePieceDetails_UsesLastEditedPieceDetail()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-PIECE-EDIT";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Edited Garment Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Blazer", 80m);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Edited Garment Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Blazer",
                        Quantity = 2,
                        PieceDetails = new List<LaundryGarmentPieceDetailDto>
                        {
                            new LaundryGarmentPieceDetailDto { PieceNo = 1, BrandName = "OldBrand", PrimaryColor = "Grey" },
                            new LaundryGarmentPieceDetailDto { PieceNo = 1, BrandName = "FinalBrand", PrimaryColor = "Black" }
                        }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(2, dto.Items.Count);

            var piece1 = dto.Items.Single(x => x.PieceNo == 1);
            var piece2 = dto.Items.Single(x => x.PieceNo == 2);
            Assert.Equal("FinalBrand", piece1.BrandName);
            Assert.Equal("Black", piece1.PrimaryColor);
            Assert.Null(piece2.BrandName);

            await using var verifyContext = _fixture.CreateDbContext();
            var persistedItems = await verifyContext.LaundryOrderItems
                .AsNoTracking()
                .Where(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo)
                .OrderBy(x => x.PieceNo)
                .ToListAsync();

            Assert.Equal("FinalBrand", persistedItems[0].BrandName);
            Assert.Null(persistedItems[1].BrandName);
        }

        [Fact]
        public async Task CreateLaundryOrder_PiecesWithEmptyGarmentDetails_DoesNotPersistMetadata()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-EMPTY-DETAILS";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Empty Metadata Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Tshirt", 30m);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Empty Metadata Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Tshirt",
                        Quantity = 1,
                        BrandName = "   ",
                        PieceDetails = new List<LaundryGarmentPieceDetailDto>
                        {
                            new LaundryGarmentPieceDetailDto
                            {
                                PieceNo = 1,
                                BrandName = "   ",
                                PrimaryColor = "",
                                Pattern = null
                            }
                        }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            var piece = Assert.Single(dto.Items);
            Assert.Null(piece.BrandName);
            Assert.Null(piece.PrimaryColor);
            Assert.Null(piece.Pattern);

            await using var verifyContext = _fixture.CreateDbContext();
            var persisted = await verifyContext.LaundryOrderItems
                .AsNoTracking()
                .SingleAsync(x => x.TenantName == tenant && x.StoreCode == store && x.OrderNo == dto.OrderNo);

            Assert.Null(persisted.BrandName);
            Assert.Null(persisted.PrimaryColor);
            Assert.Null(persisted.Pattern);
        }

        [Fact]
        public async Task LaundryOrderRetrieval_ReturnsScopedOrders_WithPieceMetadata_AndHistoricalFallbackTotals()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-RETRIEVE";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Retrieve Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Suit", 100m);

            var service = _fixture.CreateService(context);
            var createResult = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Retrieve Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Suit",
                        Quantity = 3,
                        PieceDetails = new List<LaundryGarmentPieceDetailDto>
                        {
                            new LaundryGarmentPieceDetailDto { PieceNo = 1, BrandName = "R1" },
                            new LaundryGarmentPieceDetailDto { PieceNo = 2, BrandName = "R2" },
                            new LaundryGarmentPieceDetailDto { PieceNo = 3, BrandName = "R3" }
                        }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, createResult.StatusCode);
            var createdOrder = Assert.IsType<LaundryOrderDto>(createResult.ResultSet);

            var historicalOrderNo = $"OLD-{Guid.NewGuid():N}";
            context.LaundryOrders.Add(new LaundryOrderEntity
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Retrieve Customer",
                OrderNo = historicalOrderNo,
                InvoiceNo = null,
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                WeightInKg = 0m,
                RatePerKg = 0m,
                OrderAmount = 150m,
                SubTotal = 150m,
                TaxPercent = 0m,
                TaxAmount = 0m,
                CgstAmount = 0m,
                SgstAmount = 0m,
                TotalAmount = 150m,
                Status = "Received",
                AdvanceUsed = 0m,
                PaidNow = 0m,
                PendingAmount = 150m,
                NetPayable = 150m,
                PaymentMode = "Cash",
                Notes = "Suit x2, Shirt x1",
                CreatedDate = DateTime.UtcNow.AddMinutes(-5)
            });

            context.LaundryOrders.Add(new LaundryOrderEntity
            {
                TenantName = tenant,
                StoreCode = "other-store",
                CustCode = custCode,
                CustomerName = "Foreign Store Customer",
                OrderNo = $"OS-{Guid.NewGuid():N}",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 90m,
                SubTotal = 90m,
                TotalAmount = 90m,
                Status = "Received",
                PendingAmount = 90m,
                NetPayable = 90m,
                PaymentMode = "Cash",
                CreatedDate = DateTime.UtcNow
            });

            context.LaundryOrders.Add(new LaundryOrderEntity
            {
                TenantName = "other-tenant",
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Foreign Tenant Customer",
                OrderNo = $"OT-{Guid.NewGuid():N}",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 90m,
                SubTotal = 90m,
                TotalAmount = 90m,
                Status = "Received",
                PendingAmount = 90m,
                NetPayable = 90m,
                PaymentMode = "Cash",
                CreatedDate = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            var byCustomerResult = await service.GetLaundryOrders(tenant, store, custCode);
            Assert.Equal(HttpStatusCode.OK, byCustomerResult.StatusCode);
            var byCustomer = Assert.IsType<LaundryOrderResponseDto>(byCustomerResult.ResultSet);

            Assert.Contains(byCustomer.Orders, x => x.OrderNo == createdOrder.OrderNo);
            Assert.Contains(byCustomer.Orders, x => x.OrderNo == historicalOrderNo);

            var createdRetrieved = byCustomer.Orders.Single(x => x.OrderNo == createdOrder.OrderNo);
            Assert.Equal(3, createdRetrieved.TotalPieces);
            Assert.Equal(3, createdRetrieved.Items.Count);
            Assert.Equal(new[] { 1, 2, 3 }, createdRetrieved.Items.OrderBy(x => x.PieceNo).Select(x => x.PieceNo).ToArray());

            var historicalRetrieved = byCustomer.Orders.Single(x => x.OrderNo == historicalOrderNo);
            Assert.Empty(historicalRetrieved.Items);
            Assert.Equal(3, historicalRetrieved.TotalPieces);

            var byStoreResult = await service.GetStoreLaundryOrders(tenant, store);
            Assert.Equal(HttpStatusCode.OK, byStoreResult.StatusCode);
            var byStore = Assert.IsType<LaundryOrderResponseDto>(byStoreResult.ResultSet);

            Assert.Contains(byStore.Orders, x => x.OrderNo == createdOrder.OrderNo);
            Assert.Contains(byStore.Orders, x => x.OrderNo == historicalOrderNo);
            Assert.DoesNotContain(byStore.Orders, x => x.CustomerName == "Foreign Store Customer" || x.CustomerName == "Foreign Tenant Customer");
        }

        [Fact]
        public async Task GetLaundryOrders_And_GetStoreLaundryOrders_ReturnTopUpSnapshots_FromPersistedRows()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-TOPUP-RETRIEVE";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "TopUp Retrieval Customer");

            var now = DateTime.UtcNow;
            var orderNo = $"ORD-TOP-{Guid.NewGuid():N}";
            var orderEntity = new LaundryOrderEntity
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "TopUp Retrieval Customer",
                OrderNo = orderNo,
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                OrderAmount = 135m,
                SubTotal = 135m,
                TaxPercent = 0m,
                TaxAmount = 0m,
                CgstAmount = 0m,
                SgstAmount = 0m,
                TotalAmount = 135m,
                Status = "Received",
                AdvanceUsed = 0m,
                PaidNow = 0m,
                PendingAmount = 135m,
                NetPayable = 135m,
                PaymentMode = "Cash",
                Notes = null,
                CreatedDate = now
            };
            context.LaundryOrders.Add(orderEntity);
            await context.SaveChangesAsync();

            context.LaundryOrderTopUps.AddRange(
                new LaundryOrderTopUpEntity
                {
                    LaundryOrderId = orderEntity.Id,
                    TenantName = tenant,
                    StoreCode = store,
                    OrderNo = orderNo,
                    LineNo = 1,
                    TopUpName = "Stain Removal",
                    ChargeType = "PerGarment",
                    ServiceType = "Dry Clean",
                    Category = "Men",
                    ItemName = "Shirt",
                    PieceNo = 0,
                    AppliedQuantity = 2m,
                    UnitPriceSnapshot = 10m,
                    LineAmountSnapshot = 20m,
                    CreatedDate = now
                },
                new LaundryOrderTopUpEntity
                {
                    LaundryOrderId = orderEntity.Id,
                    TenantName = tenant,
                    StoreCode = store,
                    OrderNo = orderNo,
                    LineNo = 2,
                    TopUpName = "Perfume",
                    ChargeType = "PerOrder",
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    PieceNo = 0,
                    AppliedQuantity = 1m,
                    UnitPriceSnapshot = 15m,
                    LineAmountSnapshot = 15m,
                    CreatedDate = now
                },
                new LaundryOrderTopUpEntity
                {
                    LaundryOrderId = orderEntity.Id,
                    TenantName = tenant,
                    StoreCode = "other-store",
                    OrderNo = orderNo,
                    LineNo = 1,
                    TopUpName = "Foreign Store TopUp",
                    ChargeType = "PerOrder",
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    PieceNo = 0,
                    AppliedQuantity = 1m,
                    UnitPriceSnapshot = 999m,
                    LineAmountSnapshot = 999m,
                    CreatedDate = now
                },
                new LaundryOrderTopUpEntity
                {
                    LaundryOrderId = orderEntity.Id,
                    TenantName = "other-tenant",
                    StoreCode = store,
                    OrderNo = orderNo,
                    LineNo = 1,
                    TopUpName = "Foreign Tenant TopUp",
                    ChargeType = "PerOrder",
                    ServiceType = "Dry Clean",
                    Category = null,
                    ItemName = null,
                    PieceNo = 0,
                    AppliedQuantity = 1m,
                    UnitPriceSnapshot = 999m,
                    LineAmountSnapshot = 999m,
                    CreatedDate = now
                });
            await context.SaveChangesAsync();

            var service = _fixture.CreateService(context);

            var byCustomerResult = await service.GetLaundryOrders(tenant, store, custCode);
            Assert.Equal(HttpStatusCode.OK, byCustomerResult.StatusCode);
            var byCustomer = Assert.IsType<LaundryOrderResponseDto>(byCustomerResult.ResultSet);
            var customerOrder = Assert.Single(byCustomer.Orders.Where(x => x.OrderNo == orderNo));
            Assert.Equal(35m, customerOrder.TopUpAmount);
            Assert.Equal(2, customerOrder.TopUps.Count);
            Assert.Equal(new[] { 1, 2 }, customerOrder.TopUps.OrderBy(x => x.LineNo).Select(x => x.LineNo).ToArray());
            Assert.Equal(new[] { 20m, 15m }, customerOrder.TopUps.OrderBy(x => x.LineNo).Select(x => x.LineAmountSnapshot).ToArray());

            var byStoreResult = await service.GetStoreLaundryOrders(tenant, store);
            Assert.Equal(HttpStatusCode.OK, byStoreResult.StatusCode);
            var byStore = Assert.IsType<LaundryOrderResponseDto>(byStoreResult.ResultSet);
            var storeOrder = Assert.Single(byStore.Orders.Where(x => x.OrderNo == orderNo));
            Assert.Equal(35m, storeOrder.TopUpAmount);
            Assert.Equal(2, storeOrder.TopUps.Count);
            Assert.DoesNotContain(storeOrder.TopUps, x => x.TopUpName == "Foreign Store TopUp" || x.TopUpName == "Foreign Tenant TopUp");
        }

        [Fact]
        public async Task CreateLaundryOrder_ZeroPriceWithNoTaxAndNoImmediatePayment_AllowsSubmission_WhenConfigured()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            const string custCode = "CUST-ZERO-PRICE";

            await using var context = _fixture.CreateDbContext();
            await SeedCustomerAsync(context, tenant, store, custCode, "Zero Price Customer");
            await SeedLaundryItemPriceAsync(context, tenant, store, "Dry Clean", "Men", "Promo Cloth", 0m);
            await SeedPricingRulesAsync(context, tenant, store, allowZeroPriceItems: true);
            await SeedPaymentSettingsAsync(context, tenant, store, enableCash: false, enableUpi: false, enableCard: false, enableWallet: false, defaultPaymentMode: "Cash");
            await SeedTaxSettingsAsync(context, tenant, store, "NoTax", "IN", "INR", enableTax: false, gstPercent: 0m, pricesIncludeTax: false);

            var service = _fixture.CreateService(context);
            var result = await service.CreateLaundryOrder(new LaundryOrderDto
            {
                TenantName = tenant,
                StoreCode = store,
                CustCode = custCode,
                CustomerName = "Zero Price Customer",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                PaidNow = 0m,
                PaymentMode = "Cash",
                Items = new List<LaundryOrderItemDto>
                {
                    new LaundryOrderItemDto
                    {
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Promo Cloth",
                        Quantity = 1
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var dto = Assert.IsType<LaundryOrderDto>(result.ResultSet);
            Assert.Equal(0m, dto.OrderAmount);
            Assert.Equal(0m, dto.SubTotal);
            Assert.Equal(0m, dto.TaxAmount);
            Assert.Equal(0m, dto.TotalAmount);
            Assert.Equal(0m, dto.PaidNow);
            Assert.Equal(0m, dto.PendingAmount);
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

        private static async Task SeedTopUpServiceAsync(
            MasterDbContext context,
            string tenant,
            string store,
            string topUpName,
            string chargeType,
            decimal unitPrice,
            params TopUpServiceApplicabilityEntity[] applicability)
        {
            var master = new TopUpServiceMasterEntity
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpName = topUpName,
                ChargeType = chargeType,
                UnitPrice = unitPrice,
                Description = null,
                IsActive = true,
                IsArchived = false,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            context.TopUpServiceMasters.Add(master);
            await context.SaveChangesAsync();

            if (applicability == null || applicability.Length == 0)
            {
                return;
            }

            foreach (var row in applicability)
            {
                row.TopUpServiceMasterId = master.Id;
                row.TenantName = tenant;
                row.StoreCode = store;
                row.CreatedDate = DateTime.UtcNow;
                row.ModifiedDate = DateTime.UtcNow;
                row.IsActive = row.IsActive;
            }

            context.TopUpServiceApplicabilities.AddRange(applicability);
            await context.SaveChangesAsync();
        }

        private static async Task SeedPaymentSettingsAsync(
            MasterDbContext context,
            string tenant,
            string store,
            bool enableCash,
            bool enableUpi,
            bool enableCard,
            bool enableWallet,
            string defaultPaymentMode)
        {
            context.PaymentSettings.Add(new PaymentSettingsEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableCash = enableCash,
                EnableUpi = enableUpi,
                EnableCard = enableCard,
                EnableWallet = enableWallet,
                DefaultPaymentMode = defaultPaymentMode,
                AllowPartialPayment = true,
                AllowCredit = false,
                CreditLimitAmount = 0m,
                RoundOffPayableAmount = false,
                Notes = null,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        }

        private static async Task SeedPricingRulesAsync(
            MasterDbContext context,
            string tenant,
            string store,
            bool allowZeroPriceItems)
        {
            context.PricingRules.Add(new PricingRulesEntity
            {
                TenantName = tenant,
                StoreCode = store,
                EnableExpressSurcharge = false,
                ExpressSurchargePercent = 0m,
                EnableMinimumOrder = false,
                MinimumOrderAmount = 0m,
                StandardTurnaroundHours = 24,
                ExpressTurnaroundHours = 12,
                RoundOffInvoiceTotal = false,
                AllowZeroPriceItems = allowZeroPriceItems,
                Notes = null,
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
