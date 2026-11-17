using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class TopUpServiceMasterEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string TopUpName { get; set; }

        /// <summary>
        /// "PerGarment" or "PerOrder".
        /// </summary>
        public string ChargeType { get; set; }

        public decimal UnitPrice { get; set; }

        public string Description { get; set; }

        public bool IsActive { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
