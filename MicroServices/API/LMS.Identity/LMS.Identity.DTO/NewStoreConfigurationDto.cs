using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO
{
    public class NewStoreConfigurationDto
    {
        [Required]
        public string TenantEmail { get; set; }

        [Required]
        public string StoreCode { get; set; }

        [Required]
        public string ConfigurationJson { get; set; }

        public string UpdatedBy { get; set; }
    }
}
