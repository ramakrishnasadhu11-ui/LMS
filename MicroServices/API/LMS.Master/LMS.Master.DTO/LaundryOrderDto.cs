using System;
using System.Collections.Generic;

namespace LMS.Master.DTO
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

    /// <summary>
    /// A garment line as captured on the order screen. Quantity is expanded into
    /// one tagged piece per unit when the order is saved.
    /// </summary>
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

    public class LaundryOrderDto
    {
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

        public string Status { get; set; }

        public decimal AdvanceUsed { get; set; }

        public decimal PaidNow { get; set; }

        public decimal PendingAmount { get; set; }

        public decimal NetPayable { get; set; }

        public decimal TopUpAmount { get; set; }

        public string PaymentMode { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        public List<LaundryOrderItemDto> Items { get; set; } = new List<LaundryOrderItemDto>();

        public List<LaundryOrderTopUpDto> TopUps { get; set; } = new List<LaundryOrderTopUpDto>();

        public int TotalPieces { get; set; }



    }
}
