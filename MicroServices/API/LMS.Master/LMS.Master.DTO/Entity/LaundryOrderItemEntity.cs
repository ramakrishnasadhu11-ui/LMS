using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    /// <summary>
    /// One physical garment piece belonging to an order. A posted order line with
    /// quantity 3 is expanded into three rows so each piece carries its own tag.
    /// </summary>
    public class LaundryOrderItemEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public decimal UnitPrice { get; set; }

        /// <summary>
        /// Position of this piece within its order line, starting at 1.
        /// </summary>
        public int PieceNo { get; set; }

        public string TagNo { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}
