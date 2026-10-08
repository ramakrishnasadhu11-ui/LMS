using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantStoreStatusEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        [MaxLength(32)]
        public string StoreCode { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        [MaxLength(256)]
        public string ActivatedBy { get; set; }

        public DateTime? ActivatedDate { get; set; }
    }
}
