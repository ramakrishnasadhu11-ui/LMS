using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class TaxInvoiceComponentEntity
    {
        [Key]
        public int Id { get; set; }

        public int TaxInvoiceSettingsId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string Code { get; set; }

        public string DisplayName { get; set; }

        public decimal RatePercent { get; set; }

        public string Jurisdiction { get; set; }

        public int Priority { get; set; }

        public bool IsCompound { get; set; }

        public bool IsActive { get; set; }

        public DateTime? EffectiveFromUtc { get; set; }

        public DateTime? EffectiveToUtc { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        public TaxInvoiceSettingsEntity TaxInvoiceSettings { get; set; }
    }
}
