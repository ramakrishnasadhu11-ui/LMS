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

        // New dedicated currency columns. Nullable to preserve backward-compatibility
        // and allow existing rows to be backfilled by migration.
        [MaxLength(16)]
        public string CurrencyCode { get; set; }

        [MaxLength(16)]
        public string CurrencySymbol { get; set; }

        [MaxLength(32)]
        public string CurrencyLocale { get; set; }

        public int? CurrencyDecimalDigits { get; set; }

        public DateTime CreatedDate { get; set; }

        [MaxLength(256)]
        public string CreatedBy { get; set; }

        public DateTime ModifiedDate { get; set; }

        [MaxLength(256)]
        public string ModifiedBy { get; set; }
    }
}
