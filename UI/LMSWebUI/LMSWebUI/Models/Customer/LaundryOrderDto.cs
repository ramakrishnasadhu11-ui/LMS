using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class LaundryGarmentPieceDetailDto
    {
        public int PieceNo { get; set; }

        public string BrandName { get; set; }

        public string PrimaryColor { get; set; }

        public string SecondaryColor { get; set; }

        public string Pattern { get; set; }

        public string ExistingDefects { get; set; }

        public string DefectRemarks { get; set; }

        public string SpecialInstructions { get; set; }
    }

    public class LaundryOrderItemDto
    {
        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public decimal UnitPrice { get; set; }

        public int Quantity { get; set; }

        public int PieceNo { get; set; }

        public string TagNo { get; set; }

        public string BrandName { get; set; }

        public string PrimaryColor { get; set; }

        public string SecondaryColor { get; set; }

        public string Pattern { get; set; }

        public string ExistingDefects { get; set; }

        public string DefectRemarks { get; set; }

        public string SpecialInstructions { get; set; }

        public List<LaundryGarmentPieceDetailDto> PieceDetails { get; set; } = new List<LaundryGarmentPieceDetailDto>();
    }

    public class LaundryOrderTopUpDto
    {
        public int Id { get; set; }

        public int LaundryOrderId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public int LineNo { get; set; }

        public string TopUpName { get; set; }

        public string ChargeType { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public int PieceNo { get; set; }

        public decimal AppliedQuantity { get; set; }

        public decimal UnitPriceSnapshot { get; set; }

        public decimal LineAmountSnapshot { get; set; }

        public DateTime CreatedDate { get; set; }
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

        public bool IsDeleted { get; set; }

        public decimal AdvanceUsed { get; set; }

        public decimal PaidNow { get; set; }

        public decimal PendingAmount { get; set; }

        public decimal NetPayable { get; set; }

        public decimal TopUpAmount { get; set; }

        public string PaymentMode { get; set; } = "Cash";

        public string PaymentId { get; set; }

        public string TransactionId { get; set; }

        public string PaymentTransactionId { get; set; }

        public string SettlementId { get; set; }

        public string ReceiptNo { get; set; }

        public string Notes { get; set; }

        public string OrderItemsJson { get; set; }

        public string TopUpsJson { get; set; }

        public DateTime CreatedDate { get; set; }

        public int TotalPieces { get; set; }

        public List<LaundryOrderDto> Orders { get; set; } = new List<LaundryOrderDto>();

        public List<LaundryOrderItemDto> Items { get; set; } = new List<LaundryOrderItemDto>();

        public List<LaundryOrderTopUpDto> TopUps { get; set; } = new List<LaundryOrderTopUpDto>();
    }
}
