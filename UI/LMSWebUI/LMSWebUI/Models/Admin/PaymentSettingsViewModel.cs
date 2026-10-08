using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class PaymentSettingsViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }

        [Display(Name = "Accept Cash")]
        public bool EnableCash { get; set; }

        [Display(Name = "Accept UPI")]
        public bool EnableUpi { get; set; }

        [Display(Name = "Accept Card")]
        public bool EnableCard { get; set; }

        [Display(Name = "Accept Wallet")]
        public bool EnableWallet { get; set; }

        [Display(Name = "Default Payment Mode")]
        [Required(ErrorMessage = "Default payment mode is required.")]
        public string DefaultPaymentMode { get; set; }

        [Display(Name = "Allow Partial / Advance Payment")]
        public bool AllowPartialPayment { get; set; }

        [Display(Name = "Allow Credit (Pay Later)")]
        public bool AllowCredit { get; set; }

        [Display(Name = "Credit Limit Amount")]
        [Range(0, 1000000, ErrorMessage = "Credit limit amount must be 0 or greater.")]
        public decimal CreditLimitAmount { get; set; }

        [Display(Name = "Round Off Payable Amount")]
        public bool RoundOffPayableAmount { get; set; }

        [Display(Name = "Notes")]
        [StringLength(300, ErrorMessage = "Notes cannot exceed 300 characters.")]
        public string Notes { get; set; }
    }
}
