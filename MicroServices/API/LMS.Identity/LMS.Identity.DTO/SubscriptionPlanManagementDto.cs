using System;
using System.Collections.Generic;

namespace LMS.Identity.DTO
{
    public class SubscriptionPlanDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDateUtc { get; set; }

        public DateTime? ModifiedDateUtc { get; set; }

        public string UpdatedBy { get; set; }
    }

    public class SubscriptionPlanCatalogResponseDto
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    }

    public class SubscriptionPlanPriceUpdateRequestDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }
    }

    public class SubscriptionPlanActiveStatusUpdateRequestDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public bool IsActive { get; set; }
    }
}
