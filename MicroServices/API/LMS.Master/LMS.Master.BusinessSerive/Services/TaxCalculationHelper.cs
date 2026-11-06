using System;
using System.Collections.Generic;
using System.Linq;

namespace LMS.Master.BusinessSerive.Services
{
    public static class TaxCalculationHelper
    {
        public static TaxCalculationResult Calculate(TaxCalculationRequest request)
        {
            var grossAmount = RoundMoney(Math.Max(0m, request?.OrderAmount ?? 0m));
            var mode = NormalizeMode(request?.TaxMode);

            if (request == null || !request.EnableTax || mode == "NOTAX")
            {
                return CreateNoTaxResult(grossAmount, mode);
            }

            var activeComponents = (request.Components ?? Enumerable.Empty<TaxCalculationComponent>())
                .Where(c => c != null && c.IsActive && c.RatePercent > 0m)
                .OrderBy(c => c.Priority)
                .ThenBy(c => c.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (activeComponents.Count == 0)
            {
                if (mode == "GST" && request.GstPercent > 0m)
                {
                    return CalculateLegacyGst(grossAmount, request.GstPercent, request.PricesIncludeTax);
                }

                return CreateNoTaxResult(grossAmount, mode);
            }

            return CalculateWithComponents(grossAmount, request.PricesIncludeTax, mode, activeComponents);
        }

        private static TaxCalculationResult CalculateWithComponents(decimal grossAmount, bool pricesIncludeTax, string mode, List<TaxCalculationComponent> components)
        {
            var result = new TaxCalculationResult
            {
                TaxMode = mode,
                ComponentBreakdowns = new List<TaxComponentBreakdown>()
            };

            decimal subTotal;
            decimal totalAmount;

            if (pricesIncludeTax)
            {
                totalAmount = grossAmount;
                var multiplier = GetInclusiveMultiplier(components);
                subTotal = multiplier <= 0m ? grossAmount : RoundMoney(grossAmount / multiplier);
            }
            else
            {
                subTotal = grossAmount;
                totalAmount = 0m;
            }

            var targetTotalTax = pricesIncludeTax
                ? RoundMoney(totalAmount - subTotal)
                : 0m;

            var runningTax = 0m;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                var taxableAmount = component.IsCompound ? subTotal + runningTax : subTotal;
                var roundedTaxableAmount = RoundMoney(taxableAmount);
                var taxAmount = RoundMoney(roundedTaxableAmount * component.RatePercent / 100m);

                if (pricesIncludeTax && i == components.Count - 1)
                {
                    taxAmount = RoundMoney(targetTotalTax - runningTax);
                }

                runningTax = RoundMoney(runningTax + taxAmount);

                result.ComponentBreakdowns.Add(new TaxComponentBreakdown
                {
                    Code = component.Code,
                    DisplayName = component.DisplayName,
                    RatePercent = component.RatePercent,
                    Priority = component.Priority,
                    IsCompound = component.IsCompound,
                    TaxableAmount = roundedTaxableAmount,
                    TaxAmount = taxAmount
                });
            }

            if (!pricesIncludeTax)
            {
                targetTotalTax = runningTax;
                totalAmount = RoundMoney(subTotal + targetTotalTax);
            }

            result.SubTotal = subTotal;
            result.TotalTax = targetTotalTax;
            result.TotalAmount = totalAmount;
            return result;
        }

        private static TaxCalculationResult CalculateLegacyGst(decimal grossAmount, decimal gstPercent, bool pricesIncludeTax)
        {
            var rate = gstPercent / 100m;

            decimal subTotal;
            decimal totalAmount;

            if (pricesIncludeTax)
            {
                totalAmount = grossAmount;
                subTotal = RoundMoney(grossAmount / (1m + rate));
            }
            else
            {
                subTotal = grossAmount;
                totalAmount = RoundMoney(grossAmount * (1m + rate));
            }

            var taxAmount = RoundMoney(totalAmount - subTotal);
            var cgst = RoundMoney(taxAmount / 2m);
            var sgst = RoundMoney(taxAmount - cgst);
            var splitRate = gstPercent / 2m;

            return new TaxCalculationResult
            {
                TaxMode = "GST",
                SubTotal = subTotal,
                TotalTax = taxAmount,
                TotalAmount = totalAmount,
                ComponentBreakdowns = new List<TaxComponentBreakdown>
                {
                    new TaxComponentBreakdown
                    {
                        Code = "CGST",
                        DisplayName = "Central GST",
                        RatePercent = splitRate,
                        Priority = 1,
                        IsCompound = false,
                        TaxableAmount = subTotal,
                        TaxAmount = cgst
                    },
                    new TaxComponentBreakdown
                    {
                        Code = "SGST",
                        DisplayName = "State GST",
                        RatePercent = splitRate,
                        Priority = 2,
                        IsCompound = false,
                        TaxableAmount = subTotal,
                        TaxAmount = sgst
                    }
                }
            };
        }

        private static TaxCalculationResult CreateNoTaxResult(decimal amount, string mode)
        {
            return new TaxCalculationResult
            {
                TaxMode = mode,
                SubTotal = amount,
                TotalTax = 0m,
                TotalAmount = amount,
                ComponentBreakdowns = new List<TaxComponentBreakdown>()
            };
        }

        private static decimal GetInclusiveMultiplier(IEnumerable<TaxCalculationComponent> components)
        {
            var multiplier = 1m;
            foreach (var component in components)
            {
                var rate = component.RatePercent / 100m;
                var taxFactor = component.IsCompound ? multiplier * rate : rate;
                multiplier += taxFactor;
            }

            return multiplier;
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string NormalizeMode(string taxMode)
        {
            return string.IsNullOrWhiteSpace(taxMode)
                ? "NOTAX"
                : taxMode.Trim().ToUpperInvariant();
        }
    }

    public class TaxCalculationRequest
    {
        public decimal OrderAmount { get; set; }

        public string TaxMode { get; set; }

        public bool EnableTax { get; set; }

        public bool PricesIncludeTax { get; set; }

        public decimal GstPercent { get; set; }

        public IEnumerable<TaxCalculationComponent> Components { get; set; }
    }

    public class TaxCalculationComponent
    {
        public string Code { get; set; }

        public string DisplayName { get; set; }

        public decimal RatePercent { get; set; }

        public int Priority { get; set; }

        public bool IsCompound { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class TaxCalculationResult
    {
        public string TaxMode { get; set; }

        public decimal SubTotal { get; set; }

        public decimal TotalTax { get; set; }

        public decimal TotalAmount { get; set; }

        public List<TaxComponentBreakdown> ComponentBreakdowns { get; set; }
    }

    public class TaxComponentBreakdown
    {
        public string Code { get; set; }

        public string DisplayName { get; set; }

        public decimal RatePercent { get; set; }

        public int Priority { get; set; }

        public bool IsCompound { get; set; }

        public decimal TaxableAmount { get; set; }

        public decimal TaxAmount { get; set; }
    }
}
