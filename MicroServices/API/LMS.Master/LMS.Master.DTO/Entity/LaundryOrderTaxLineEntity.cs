using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class LaundryOrderTaxLineEntity
    {
        [Key]
        public int Id { get; set; }

        public int LaundryOrderId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public int LineNo { get; set; }

        public string TaxMode { get; set; }

        public string CurrencyCode { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentDisplayName { get; set; }

        public string Jurisdiction { get; set; }

        public int Priority { get; set; }

        public bool IsCompound { get; set; }

        public decimal RatePercent { get; set; }

        public decimal TaxableBaseAmount { get; set; }

        public decimal TaxAmount { get; set; }
    }
}
