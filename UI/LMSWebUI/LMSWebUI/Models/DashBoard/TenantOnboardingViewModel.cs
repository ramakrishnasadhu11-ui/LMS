using System;

namespace LMSWebUI.Models.DashBoard
{
    public class TenantOnboardingViewModel
    {
        public string TenantName { get; set; }

        public string TenantStore { get; set; }

        public string PlanCode { get; set; }

        public string SubscriptionStatus { get; set; }

        public DateTime? TrialEndUtc { get; set; }

        public int? TrialDaysRemaining { get; set; }

        public bool IsOnboardingCompleted { get; set; }

        public bool IsTrialExpired => TrialDaysRemaining.HasValue && TrialDaysRemaining.Value < 0;
    }
}
