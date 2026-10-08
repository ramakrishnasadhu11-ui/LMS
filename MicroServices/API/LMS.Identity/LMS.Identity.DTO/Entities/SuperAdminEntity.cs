using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    /// <summary>
    /// A global administrator account that exists independently of any tenant.
    /// Super admins approve store activation requests across all tenants.
    /// </summary>
    public class SuperAdminEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string Email { get; set; }

        [Required]
        [MaxLength(512)]
        public string Password { get; set; }

        [MaxLength(128)]
        public string DisplayName { get; set; }

        public bool IsActive { get; set; }

        public bool IsPasswordChanged { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime? LastLoginDate { get; set; }
    }
}
