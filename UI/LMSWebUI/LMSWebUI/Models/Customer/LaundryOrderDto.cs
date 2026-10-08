using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class LaundryOrderItemDto
    {
        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public decimal UnitPrice { get; set; }

        public int Quantity { get; set; }

        public int PieceNo { get; set; }

        public string TagNo { get; set; }
    }

    public class LaundryOrderDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CustCode { get; set; }

        public string CustomerName { get; set; }

        public string OrderNo { get; set; }

        public string ServiceType { get; set; } = "Dry Clean";

        public string OrderMode { get; set; } = "pieces";

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

        public string Status { get; set; }

        public decimal AdvanceUsed { get; set; }

        public decimal PaidNow { get; set; }

        public decimal PendingAmount { get; set; }

        public decimal NetPayable { get; set; }

        public string PaymentMode { get; set; } = "Cash";

        public string Notes { get; set; }

        public string OrderItemsJson { get; set; }

        public DateTime CreatedDate { get; set; }

        public int TotalPieces { get; set; }

        public List<LaundryOrderDto> Orders { get; set; } = new List<LaundryOrderDto>();

        public List<LaundryOrderItemDto> Items { get; set; } = new List<LaundryOrderItemDto>();
    }
}
