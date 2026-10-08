using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class TaxInvoiceSettingsViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }

        /// <summary>
        /// Registered company name of the tenant. Display only; never persisted with the settings.
        /// </summary>
        public string CompanyName { get; set; }

        [Display(Name = "Apply Tax On Invoices")]
        public bool EnableTax { get; set; }

        [Display(Name = "GST Percentage (%)")]
        [Range(0, 100, ErrorMessage = "GST percentage must be between 0 and 100.")]
        public decimal GstPercent { get; set; }

        [Display(Name = "Item Prices Already Include Tax")]
        public bool PricesIncludeTax { get; set; }

        [Display(Name = "Invoice Prefix")]
        [StringLength(16, ErrorMessage = "Invoice prefix cannot exceed 16 characters.")]
        public string InvoicePrefix { get; set; }

        [Display(Name = "Next Invoice Number")]
        [Range(1, int.MaxValue, ErrorMessage = "Next invoice number must be 1 or greater.")]
        public int NextInvoiceNumber { get; set; }

        [Display(Name = "Invoice Number Padding")]
        [Range(1, 12, ErrorMessage = "Invoice number padding must be between 1 and 12.")]
        public int InvoiceNumberPadding { get; set; }

        [Display(Name = "Reset Invoice Number Every Year")]
        public bool ResetInvoiceNumberYearly { get; set; }

        [Display(Name = "Notes")]
        [StringLength(300, ErrorMessage = "Notes cannot exceed 300 characters.")]
        public string Notes { get; set; }
    }
}
