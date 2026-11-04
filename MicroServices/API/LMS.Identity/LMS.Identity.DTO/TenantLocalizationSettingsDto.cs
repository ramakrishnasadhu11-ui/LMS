using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO
{
    public class TenantLocalizationSettingsDto
    {
        [Required]
        public string TimeZoneIana { get; set; }

        [Required]
        [StringLength(8)]
        public string PhoneDialingCode { get; set; }
    }
}
