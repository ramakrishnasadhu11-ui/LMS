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

        [Fact]
        public void BuildBillLines_IncludesTopUpSnapshotLines_AndTopUpSummary()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-TOP-1",
                InvoiceNo = "INV-TOP-1",
                CustCode = "C100",
                CustomerName = "TopUp Customer",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                SubTotal = 135m,
                TaxAmount = 0m,
                TotalAmount = 135m,
                TopUpAmount = 35m,
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto
                    {
                        LineNo = 1,
                        TopUpName = "Stain Removal",
                        ChargeType = "PerGarment",
                        AppliedQuantity = 2m,
                        UnitPriceSnapshot = 10m,
                        LineAmountSnapshot = 20m
                    },
                    new LaundryOrderTopUpDto
                    {
                        LineNo = 2,
                        TopUpName = "Perfume",
                        ChargeType = "PerOrder",
                        AppliedQuantity = 1m,
                        UnitPriceSnapshot = 15m,
                        LineAmountSnapshot = 15m
                    }
                }
            };

            var lines = InvokeBuildBillLines(context);
            var joined = string.Join("\n", lines);

            Assert.Contains("Top-up: Stain Removal (PerGarment)", joined);
            Assert.Contains("Top-up: Perfume (PerOrder)", joined);
            Assert.Contains("Top-ups:", joined);
        }

        [Fact]
        public void BuildBillHtml_IncludesTopUpSnapshotRows_AndTopUpSummary()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-TOP-2",
                InvoiceNo = "INV-TOP-2",
                CustCode = "C101",
                CustomerName = "TopUp Html",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                SubTotal = 135m,
                TaxAmount = 0m,
                TotalAmount = 135m,
                TopUpAmount = 35m,
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto
                    {
                        LineNo = 1,
                        TopUpName = "Stain Removal",
                        ChargeType = "PerGarment",
                        AppliedQuantity = 2m,
                        UnitPriceSnapshot = 10m,
                        LineAmountSnapshot = 20m
                    }
                }
            };

            var html = InvokeBuildBillHtml(context);

            Assert.Contains("Top-up: PerGarment", html);
            Assert.Contains("Stain Removal", html);
            Assert.Contains("Top-ups", html);
        }

        [Fact]
        public void BuildStyledInvoicePdf_WithTopUps_GeneratesNonEmptyPdf()
        {
            var context = new OrderCompletionContextDto
            {
                OrderNo = "ORD-TOP-3",
                InvoiceNo = "INV-TOP-3",
                CustCode = "C102",
                CustomerName = "TopUp Pdf",
                StoreCode = "S1",
                ServiceType = "Dry Clean",
                OrderMode = "pieces",
                CreatedDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                SubTotal = 135m,
                TaxAmount = 0m,
                TotalAmount = 135m,
                TopUpAmount = 35m,
                TopUps = new List<LaundryOrderTopUpDto>
                {
                    new LaundryOrderTopUpDto
                    {
                        LineNo = 1,
                        TopUpName = "Perfume",
                        ChargeType = "PerOrder",
                        AppliedQuantity = 1m,
                        UnitPriceSnapshot = 15m,
                        LineAmountSnapshot = 15m
                    }
                }
            };

            var pdf = InvokeBuildStyledInvoicePdf(context);

            Assert.NotNull(pdf);
            Assert.True(pdf.Length > 0);
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

        private static string InvokeBuildBillHtml(OrderCompletionContextDto context)
        {
            var method = typeof(CustomerController).GetMethod(
                "BuildBillHtml",
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

            var html = result as string;
            Assert.False(string.IsNullOrWhiteSpace(html));
            return html;
        }

        private static byte[] InvokeBuildStyledInvoicePdf(OrderCompletionContextDto context)
        {
            var method = typeof(CustomerController).GetMethod(
                "BuildStyledInvoicePdf",
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

            var pdf = result as byte[];
            Assert.NotNull(pdf);
            return pdf;
        }
    }
}
