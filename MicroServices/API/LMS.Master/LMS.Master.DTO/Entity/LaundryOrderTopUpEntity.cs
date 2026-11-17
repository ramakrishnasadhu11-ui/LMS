using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    /// <summary>
    /// Immutable top-up snapshot rows captured at order creation.
    /// </summary>
    public class LaundryOrderTopUpEntity
    {
        [Key]
        public int Id { get; set; }

        public int LaundryOrderId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public int LineNo { get; set; }

        public string TopUpName { get; set; }

        /// <summary>
        /// "PerGarment" or "PerOrder" as captured at order time.
        /// </summary>
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
}
