using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO
{
    public class TenantLoginRequest
    {
        [Required]
        [EmailAddress]
        public string EMail { get; set; }

        [Required]
        public string Password { get; set; }

        public string StoreCode { get; set; }
    }
}
