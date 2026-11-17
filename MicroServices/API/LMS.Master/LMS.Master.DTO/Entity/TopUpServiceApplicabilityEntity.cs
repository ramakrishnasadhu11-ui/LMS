using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class TopUpServiceApplicabilityEntity
    {
        [Key]
        public int Id { get; set; }

        public int TopUpServiceMasterId { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string ServiceType { get; set; }

        public string Category { get; set; }

        public string ItemName { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
