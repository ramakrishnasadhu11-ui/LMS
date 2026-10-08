using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class LaundryOrderEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CustCode { get; set; }

        public string CustomerName { get; set; }

        public string OrderNo { get; set; }

        public string ServiceType { get; set; }

        public string OrderMode { get; set; }

        public decimal WeightInKg { get; set; }

        public decimal RatePerKg { get; set; }

        public decimal OrderAmount { get; set; }

        public decimal SubTotal { get; set; }

        public decimal TaxPercent { get; set; }

        public decimal TaxAmount { get; set; }

        public decimal CgstAmount { get; set; }

        public decimal SgstAmount { get; set; }

        public decimal TotalAmount { get; set; }

        public string InvoiceNo { get; set; }

        /// <summary>
        /// Snapshot of the store workflow status the order currently sits in.
        /// </summary>
        public string Status { get; set; }

        public decimal AdvanceUsed { get; set; }

        public decimal PaidNow { get; set; }

        public decimal PendingAmount { get; set; }

        public decimal NetPayable { get; set; }

        public string PaymentMode { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        
    }
}
