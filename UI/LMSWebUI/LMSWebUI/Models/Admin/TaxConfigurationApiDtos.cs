using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Admin
{
    public class TaxConfigurationDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string TaxMode { get; set; }
        public string CountryCode { get; set; }
        public string CurrencyCode { get; set; }
        public bool IsPricesInclusive { get; set; }
        public bool EnableTax { get; set; }
        public decimal GstPercent { get; set; }
        public bool PricesIncludeTax { get; set; }
        public string InvoicePrefix { get; set; }
        public int NextInvoiceNumber { get; set; }
        public int InvoiceNumberPadding { get; set; }
        public bool ResetInvoiceNumberYearly { get; set; }
        public string Notes { get; set; }
        public string ConcurrencyToken { get; set; }
        public string LegacyFallbackSource { get; set; }
        public List<TaxInvoiceComponentDto> Components { get; set; } = new List<TaxInvoiceComponentDto>();
    }

    public class TaxConfigurationUpsertRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string TaxMode { get; set; }
        public string CountryCode { get; set; }
        public string CurrencyCode { get; set; }
        public bool IsPricesInclusive { get; set; }
        public bool? EnableTax { get; set; }
        public decimal? GstPercent { get; set; }
        public bool? PricesIncludeTax { get; set; }
        public string InvoicePrefix { get; set; }
        public int? NextInvoiceNumber { get; set; }
        public int? InvoiceNumberPadding { get; set; }
        public bool? ResetInvoiceNumberYearly { get; set; }
        public string Notes { get; set; }
        public string ConcurrencyToken { get; set; }
        public List<TaxInvoiceComponentDto> Components { get; set; } = new List<TaxInvoiceComponentDto>();
    }

    public class TaxInvoiceComponentDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public decimal RatePercent { get; set; }
        public string Jurisdiction { get; set; }
        public int Priority { get; set; }
        public bool IsCompound { get; set; }
        public bool IsActive { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
        public string ConcurrencyToken { get; set; }
    }

    public class TaxInvoiceComponentUpsertRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public int? ComponentId { get; set; }
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public decimal RatePercent { get; set; }
        public string Jurisdiction { get; set; }
        public int Priority { get; set; }
        public bool IsCompound { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
        public string ConcurrencyToken { get; set; }
    }

    public class TaxInvoiceComponentDeactivateRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public int ComponentId { get; set; }
        public string ConcurrencyToken { get; set; }
    }

    public class TaxModeResolutionDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string TaxMode { get; set; }
        public string ResolutionSource { get; set; }
    }
}
