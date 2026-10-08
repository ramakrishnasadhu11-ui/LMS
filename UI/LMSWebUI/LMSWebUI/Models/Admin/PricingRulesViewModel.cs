using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class PricingRulesViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }

        [Display(Name = "Enable Express Surcharge")]
        public bool EnableExpressSurcharge { get; set; }

        [Display(Name = "Express Surcharge (%)")]
        [Range(0, 100, ErrorMessage = "Express surcharge must be between 0 and 100.")]
        public decimal ExpressSurchargePercent { get; set; }

        [Display(Name = "Enable Minimum Order Value")]
        public bool EnableMinimumOrder { get; set; }

        [Display(Name = "Minimum Order Amount")]
        [Range(0, 1000000, ErrorMessage = "Minimum order amount must be 0 or greater.")]
        public decimal MinimumOrderAmount { get; set; }

        [Display(Name = "Standard Turnaround (Hours)")]
        [Range(1, 240, ErrorMessage = "Standard turnaround must be between 1 and 240 hours.")]
        public int StandardTurnaroundHours { get; set; }

        [Display(Name = "Express Turnaround (Hours)")]
        [Range(1, 240, ErrorMessage = "Express turnaround must be between 1 and 240 hours.")]
        public int ExpressTurnaroundHours { get; set; }

        [Display(Name = "Round Off Invoice Total")]
        public bool RoundOffInvoiceTotal { get; set; }

        [Display(Name = "Notes")]
        [StringLength(300, ErrorMessage = "Notes cannot exceed 300 characters.")]
        public string Notes { get; set; }
    }
}
