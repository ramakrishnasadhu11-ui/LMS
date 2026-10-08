using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO
{
    public class CreateTenantStoreDto
    {
        [Required]
        public string TenantName { get; set; }

        [Required]
        public string StoreCode { get; set; }
    }
}
