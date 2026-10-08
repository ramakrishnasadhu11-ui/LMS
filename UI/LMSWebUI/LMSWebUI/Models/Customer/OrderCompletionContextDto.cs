using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class OrderCompletionContextDto
    {
        public string OrderNo { get; set; }
        public string InvoiceNo { get; set; }
        public string CustCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public string StoreCode { get; set; }
        public string TenantName { get; set; }
        public string OrderMode { get; set; }
        public decimal OrderAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal AdvanceUsed { get; set; }
        public decimal PaidNow { get; set; }
        public decimal PendingAmount { get; set; }
        public decimal NetPayable { get; set; }
        public string PaymentMode { get; set; }
        public string ServiceType { get; set; }
        public int TotalPieces { get; set; }
        public DateTime CreatedDate { get; set; }
        public List<LaundryOrderItemDto> Items { get; set; } = new List<LaundryOrderItemDto>();
    }
}
