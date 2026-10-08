using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace LMSWebUI.Models.Admin
{
    public class StoreConfigurationViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public bool IsExistingRecord { get; set; }

        /// <summary>
        /// Populated when the existing configuration could not be loaded from the API.
        /// Never persisted as part of the configuration payload.
        /// </summary>
        [JsonIgnore]
        public string LoadErrorMessage { get; set; }

        [Display(Name = "Store Name")]
        [Required(ErrorMessage = "Store name is required.")]
        [StringLength(120, ErrorMessage = "Store name cannot exceed 120 characters.")]
        public string StoreName { get; set; }

        [Display(Name = "Contact Person")]
        [Required(ErrorMessage = "Contact person is required.")]
        [StringLength(100, ErrorMessage = "Contact person cannot exceed 100 characters.")]
        public string ContactPerson { get; set; }

        [Display(Name = "Phone Number")]
        [Required(ErrorMessage = "Phone number is required.")]
        [RegularExpression("^[0-9+()\\-\\s]{7,20}$", ErrorMessage = "Please enter a valid phone number.")]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        public string PhoneNumber { get; set; }

        [Display(Name = "Email")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
        public string Email { get; set; }

        [Display(Name = "Address Line 1")]
        [Required(ErrorMessage = "Address line 1 is required.")]
        [StringLength(200, ErrorMessage = "Address line 1 cannot exceed 200 characters.")]
        public string AddressLine1 { get; set; }

        [Display(Name = "Address Line 2")]
        [StringLength(200, ErrorMessage = "Address line 2 cannot exceed 200 characters.")]
        public string AddressLine2 { get; set; }

        [Display(Name = "City")]
        [Required(ErrorMessage = "City is required.")]
        [StringLength(80, ErrorMessage = "City cannot exceed 80 characters.")]
        public string City { get; set; }

        [Display(Name = "State")]
        [Required(ErrorMessage = "State is required.")]
        [StringLength(80, ErrorMessage = "State cannot exceed 80 characters.")]
        public string State { get; set; }

        [Display(Name = "Postal Code")]
        [Required(ErrorMessage = "Postal code is required.")]
        [RegularExpression("^[A-Za-z0-9\\-\\s]{4,12}$", ErrorMessage = "Please enter a valid postal code.")]
        [StringLength(20, ErrorMessage = "Postal code cannot exceed 20 characters.")]
        public string PostalCode { get; set; }

        [Display(Name = "Opening Time")]
        [Required(ErrorMessage = "Opening time is required.")]
        public string OpeningTime { get; set; }

        [Display(Name = "Closing Time")]
        [Required(ErrorMessage = "Closing time is required.")]
        public string ClosingTime { get; set; }

        [Display(Name = "Weekly Off Day")]
        [StringLength(20, ErrorMessage = "Weekly off day cannot exceed 20 characters.")]
        public string WeeklyOffDay { get; set; }

        [Display(Name = "Notes")]
        [StringLength(300, ErrorMessage = "Notes cannot exceed 300 characters.")]
        public string Notes { get; set; }
    }
}
