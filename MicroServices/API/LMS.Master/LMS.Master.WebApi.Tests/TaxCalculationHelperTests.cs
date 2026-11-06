using LMS.Master.BusinessSerive.Services;
using System.Linq;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class TaxCalculationHelperTests
    {
        [Fact]
        public void Calculate_Exclusive_VatWithMultipleComponents_ReturnsExpectedTotals()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 100m,
                TaxMode = "VAT",
                EnableTax = true,
                PricesIncludeTax = false,
                Components = new[]
                {
                    new TaxCalculationComponent { Code = "VAT-A", DisplayName = "VAT A", RatePercent = 10m, Priority = 2, IsCompound = false, IsActive = true },
                    new TaxCalculationComponent { Code = "VAT-B", DisplayName = "VAT B", RatePercent = 5m, Priority = 1, IsCompound = false, IsActive = true }
                }
            });

            Assert.Equal(100m, result.SubTotal);
            Assert.Equal(15m, result.TotalTax);
            Assert.Equal(115m, result.TotalAmount);
            Assert.Equal(2, result.ComponentBreakdowns.Count);
            Assert.Equal("VAT-B", result.ComponentBreakdowns[0].Code);
            Assert.Equal(5m, result.ComponentBreakdowns[0].TaxAmount);
            Assert.Equal("VAT-A", result.ComponentBreakdowns[1].Code);
            Assert.Equal(10m, result.ComponentBreakdowns[1].TaxAmount);
        }

        [Fact]
        public void Calculate_Inclusive_Vat_ReturnsExpectedSubTotalAndTax()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 118m,
                TaxMode = "VAT",
                EnableTax = true,
                PricesIncludeTax = true,
                Components = new[]
                {
                    new TaxCalculationComponent { Code = "VAT", DisplayName = "VAT", RatePercent = 18m, Priority = 1, IsCompound = false, IsActive = true }
                }
            });

            Assert.Equal(100m, result.SubTotal);
            Assert.Equal(18m, result.TotalTax);
            Assert.Equal(118m, result.TotalAmount);
            Assert.Single(result.ComponentBreakdowns);
            Assert.Equal(18m, result.ComponentBreakdowns[0].TaxAmount);
        }

        [Fact]
        public void Calculate_Exclusive_CompoundTax_UsesTaxOnTax()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 100m,
                TaxMode = "SalesTax",
                EnableTax = true,
                PricesIncludeTax = false,
                Components = new[]
                {
                    new TaxCalculationComponent { Code = "BASE", DisplayName = "Base", RatePercent = 10m, Priority = 1, IsCompound = false, IsActive = true },
                    new TaxCalculationComponent { Code = "COMP", DisplayName = "Compound", RatePercent = 10m, Priority = 2, IsCompound = true, IsActive = true }
                }
            });

            Assert.Equal(100m, result.SubTotal);
            Assert.Equal(21m, result.TotalTax);
            Assert.Equal(121m, result.TotalAmount);

            Assert.Equal(100m, result.ComponentBreakdowns[0].TaxableAmount);
            Assert.Equal(10m, result.ComponentBreakdowns[0].TaxAmount);

            Assert.Equal(110m, result.ComponentBreakdowns[1].TaxableAmount);
            Assert.Equal(11m, result.ComponentBreakdowns[1].TaxAmount);
        }

        [Fact]
        public void Calculate_NoTaxMode_ReturnsZeroTaxAndNoBreakdowns()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 250m,
                TaxMode = "NoTax",
                EnableTax = true,
                PricesIncludeTax = false,
                Components = new[]
                {
                    new TaxCalculationComponent { Code = "IGN", DisplayName = "Ignored", RatePercent = 18m, Priority = 1, IsCompound = false, IsActive = true }
                }
            });

            Assert.Equal(250m, result.SubTotal);
            Assert.Equal(0m, result.TotalTax);
            Assert.Equal(250m, result.TotalAmount);
            Assert.Empty(result.ComponentBreakdowns);
        }

        [Fact]
        public void Calculate_Rounding_AwayFromZero_IsApplied()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 10.02m,
                TaxMode = "SalesTax",
                EnableTax = true,
                PricesIncludeTax = false,
                Components = new[]
                {
                    new TaxCalculationComponent { Code = "T1", DisplayName = "Tax", RatePercent = 12.525m, Priority = 1, IsCompound = false, IsActive = true }
                }
            });

            Assert.Equal(10.02m, result.SubTotal);
            Assert.Equal(1.26m, result.TotalTax);
            Assert.Equal(11.28m, result.TotalAmount);
            Assert.Equal(1.26m, result.ComponentBreakdowns.Single().TaxAmount);
        }

        [Fact]
        public void Calculate_GstWithoutComponents_UsesLegacyFallbackBehavior()
        {
            var result = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = 118m,
                TaxMode = "GST",
                EnableTax = true,
                PricesIncludeTax = true,
                GstPercent = 18m,
                Components = new TaxCalculationComponent[0]
            });

            Assert.Equal(100m, result.SubTotal);
            Assert.Equal(18m, result.TotalTax);
            Assert.Equal(118m, result.TotalAmount);
            Assert.Equal(2, result.ComponentBreakdowns.Count);
            Assert.Equal("CGST", result.ComponentBreakdowns[0].Code);
            Assert.Equal(9m, result.ComponentBreakdowns[0].TaxAmount);
            Assert.Equal("SGST", result.ComponentBreakdowns[1].Code);
            Assert.Equal(9m, result.ComponentBreakdowns[1].TaxAmount);
        }
    }
}
