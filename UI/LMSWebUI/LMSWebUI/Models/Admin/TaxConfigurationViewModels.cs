using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class TaxConfigurationComponentViewModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(32)]
        public string Code { get; set; }

        [Required]
        [StringLength(128)]
        public string DisplayName { get; set; }

        [Range(0, 100)]
        public decimal RatePercent { get; set; }

        [StringLength(64)]
        public string Jurisdiction { get; set; }

        public int Priority { get; set; } = 1;

        public bool IsCompound { get; set; }

        public bool IsActive { get; set; } = true;

        public string EffectiveFromUtcText { get; set; }

        public string EffectiveToUtcText { get; set; }

        public string ConcurrencyToken { get; set; }
    }

    public class TaxComponentEditorViewModel
    {
        public bool IsEdit { get; set; }

        public int? ComponentId { get; set; }

        [Required]
        [StringLength(32)]
        public string Code { get; set; }

        [Required]
        [StringLength(128)]
        public string DisplayName { get; set; }

        [Range(0, 100)]
        public decimal RatePercent { get; set; }

        [StringLength(64)]
        public string Jurisdiction { get; set; }

        [Range(1, int.MaxValue)]
        public int Priority { get; set; } = 1;

        public bool IsCompound { get; set; }

        public bool IsActive { get; set; } = true;

        public string EffectiveFromUtcText { get; set; }

        public string EffectiveToUtcText { get; set; }

        public string ConcurrencyToken { get; set; }
    }

    public class TaxConfigurationViewModel
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CompanyName { get; set; }

        [Required]
        [StringLength(24)]
        public string TaxMode { get; set; } = "GST";

        [Required]
        [StringLength(2)]
        public string CountryCode { get; set; } = "IN";

        [Required]
        [StringLength(8)]
        public string CurrencyCode { get; set; } = "INR";

        public bool IsPricesInclusive { get; set; }

        public bool EnableTax { get; set; }

        [Range(0, 100)]
        public decimal GstPercent { get; set; }

        public bool PricesIncludeTax { get; set; }

        [Required]
        [StringLength(16)]
        public string InvoicePrefix { get; set; }

        [Range(1, int.MaxValue)]
        public int NextInvoiceNumber { get; set; } = 1;

        [Range(1, 12)]
        public int InvoiceNumberPadding { get; set; } = 4;

        public bool ResetInvoiceNumberYearly { get; set; }

        [StringLength(300)]
        public string Notes { get; set; }

        public string ConcurrencyToken { get; set; }

        public string LegacyFallbackSource { get; set; }

        public string ResolvedTaxMode { get; set; }

        public string RecommendedTaxMode { get; set; }

        public string TenantRegisteredCountryCode { get; set; }

        public string OriginalCountryCode { get; set; }

        public bool TaxModeOverrideConfirmed { get; set; }

        public bool NoTaxConfirmed { get; set; }

        public bool CountryChangeAcknowledged { get; set; }

        [Range(0, 100)]
        public decimal? CgstRatePercent { get; set; }

        [Range(0, 100)]
        public decimal? SgstRatePercent { get; set; }

        public List<TaxConfigurationComponentViewModel> Components { get; set; } = new List<TaxConfigurationComponentViewModel>();

        public TaxComponentEditorViewModel ComponentEditor { get; set; } = new TaxComponentEditorViewModel();

        public bool IsAutoCreatedDefault { get; set; }
    }
}
