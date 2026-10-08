using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class PricingRulesEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public bool EnableExpressSurcharge { get; set; }

        public decimal ExpressSurchargePercent { get; set; }

        public bool EnableMinimumOrder { get; set; }

        public decimal MinimumOrderAmount { get; set; }

        public int StandardTurnaroundHours { get; set; }

        public int ExpressTurnaroundHours { get; set; }

        public bool RoundOffInvoiceTotal { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
