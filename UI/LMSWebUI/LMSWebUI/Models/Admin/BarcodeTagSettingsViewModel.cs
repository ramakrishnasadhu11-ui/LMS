using System.ComponentModel.DataAnnotations;

namespace LMSWebUI.Models.Admin
{
    public class BarcodeTagSettingsViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }

        /// <summary>
        /// Registered company name of the tenant. Display only; never persisted with the settings.
        /// </summary>
        public string CompanyName { get; set; }

        [Display(Name = "Print Barcode Tags For Orders")]
        public bool EnableTagging { get; set; }

        [Display(Name = "Tag Prefix")]
        [StringLength(16, ErrorMessage = "Tag prefix cannot exceed 16 characters.")]
        public string TagPrefix { get; set; }

        [Display(Name = "Next Tag Number")]
        [Range(1, int.MaxValue, ErrorMessage = "Next tag number must be 1 or greater.")]
        public int NextTagNumber { get; set; }

        [Display(Name = "Tag Number Padding")]
        [Range(1, 12, ErrorMessage = "Tag number padding must be between 1 and 12.")]
        public int TagNumberPadding { get; set; }

        [Display(Name = "Reset Tag Number Every Year")]
        public bool ResetTagNumberYearly { get; set; }

        [Display(Name = "Notes")]
        [StringLength(300, ErrorMessage = "Notes cannot exceed 300 characters.")]
        public string Notes { get; set; }
    }
}
