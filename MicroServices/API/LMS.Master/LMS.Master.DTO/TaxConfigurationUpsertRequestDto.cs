using System.Collections.Generic;

namespace LMS.Master.DTO
{
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
}
