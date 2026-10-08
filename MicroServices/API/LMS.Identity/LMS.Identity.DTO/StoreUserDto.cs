using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO
{
    public class StoreUserDto
    {
        [Required]
        public string TenantEmail { get; set; }

        [Required]
        public string StoreCode { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string Password { get; set; }

        public string Role { get; set; } = "StoreUser";

        public bool IsActive { get; set; } = true;

        public string CreatedBy { get; set; }
    }
}
