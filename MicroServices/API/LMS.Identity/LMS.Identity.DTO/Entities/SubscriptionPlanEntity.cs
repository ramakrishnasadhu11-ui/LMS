using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class SubscriptionPlanEntity
    {
        [Key]
        public int Id { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDateUtc { get; set; }

        public DateTime? ModifiedDateUtc { get; set; }

        public string UpdatedBy { get; set; }
    }
}
