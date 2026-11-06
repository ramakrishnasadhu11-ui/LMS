using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using LMSWebUI.Controllers;
using LMSWebUI.Models.Customer;
using LMSWebUI.Services;
using Xunit;

namespace LMSWebUI.Tests
{
    public class InvoiceTaxBreakdownTests
    {
        [Fact]
        public void BuildBillLines_UsesSnapshotTaxLines_WhenAvailable()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-1",
                InvoiceNo = "INV-1",
                CustCode = "C001",
                CustomerName = "John",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                OrderAmount = 100m,
                SubTotal = 100m,
                TaxAmount = 21m,
                TotalAmount = 121m,
                TaxLines = new List<LaundryOrderTaxLineDto>
                {
                    new LaundryOrderTaxLineDto { LineNo = 1, ComponentCode = "BASE", ComponentDisplayName = "Base Tax", TaxAmount = 10m },
                    new LaundryOrderTaxLineDto { LineNo = 2, ComponentCode = "COMP", ComponentDisplayName = "Compound Tax", TaxAmount = 11m }
                }
            };

            var lines = InvokeBuildBillLines(context);
            var joined = string.Join("\n", lines);

            Assert.Contains("Base Tax", joined);
            Assert.Contains("Compound Tax", joined);
            Assert.Contains("Total Tax", joined);
            Assert.DoesNotContain("Tax (", joined);
        }

        [Fact]
        public void BuildBillLines_UsesHistoricalGstFallback_WhenNoSnapshotTaxLines()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-2",
                InvoiceNo = "INV-2",
                CustCode = "C002",
                CustomerName = "Jane",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                SubTotal = 100m,
                TaxPercent = 18m,
                TaxAmount = 18m,
                CgstAmount = 9m,
                SgstAmount = 9m,
                TotalAmount = 118m,
                TaxLines = new List<LaundryOrderTaxLineDto>()
            };

            var lines = InvokeBuildBillLines(context);
            var joined = string.Join("\n", lines);

            Assert.Contains("CGST", joined);
            Assert.Contains("SGST", joined);
            Assert.Contains("Total Tax", joined);
        }

        [Fact]
        public void BuildBillLines_NoTaxOrder_ShowsZeroTotalTax()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-3",
                InvoiceNo = "INV-3",
                CustCode = "C003",
                CustomerName = "NoTax",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                SubTotal = 100m,
                TaxAmount = 0m,
                TotalAmount = 100m,
                TaxLines = new List<LaundryOrderTaxLineDto>()
            };

            var lines = InvokeBuildBillLines(context);
            var joined = string.Join("\n", lines);

            Assert.Contains("Total Tax", joined);
            Assert.DoesNotContain("CGST", joined);
            Assert.DoesNotContain("SGST", joined);
            Assert.Contains("0.00", joined);
        }

        private static List<string> InvokeBuildBillLines(OrderCompletionContextDto context)
        {
            var method = typeof(CustomerController).GetMethod(
                "BuildBillLines",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = method.Invoke(
                null,
                new object[]
                {
                    context,
                    null,
                    null,
                    "Laundry Service",
                    null
                });

            var lines = result as List<string>;
            Assert.NotNull(lines);
            return lines;
        }
    }
}
