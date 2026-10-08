using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class StoreConfigurationEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        [MaxLength(32)]
        public string StoreCode { get; set; }

        [Required]
        public string ConfigurationJson { get; set; }

        public DateTime CreatedDate { get; set; }

        [MaxLength(256)]
        public string CreatedBy { get; set; }

        public DateTime ModifiedDate { get; set; }

        [MaxLength(256)]
        public string ModifiedBy { get; set; }
    }
}
