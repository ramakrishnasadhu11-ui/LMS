using System;

namespace LMS.Master.DTO
{
    public class TaxInvoiceComponentUpsertRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public int? ComponentId { get; set; }

        public string Code { get; set; }

        public string DisplayName { get; set; }

        public decimal RatePercent { get; set; }

        public string Jurisdiction { get; set; }

        public int Priority { get; set; }

        public bool IsCompound { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFromUtc { get; set; }

        public DateTime? EffectiveToUtc { get; set; }

        public string ConcurrencyToken { get; set; }
    }
}
