using System;

namespace LMSWebUI.Models.DashBoard
{
    public class TenantOnboardingViewModel
    {
        public string TenantName { get; set; }

        public string TenantStore { get; set; }

        public string StorePlanCode { get; set; }

        public string StoreSubscriptionStatus { get; set; }

        public DateTime? StoreTrialEndUtc { get; set; }

        public int? StoreTrialDaysRemaining { get; set; }

        public string PlanCode { get; set; }

        public string SubscriptionStatus { get; set; }

        public string AccountStatus { get; set; }

        public string PlanName { get; set; }

        public DateTime? TrialEndUtc { get; set; }

        public DateTime? SubscriptionExpiryUtc { get; set; }

        public string RegisteredDomains { get; set; }

        public string BackupServiceStatus { get; set; }

        public int? TrialDaysRemaining { get; set; }

        public bool IsOnboardingCompleted { get; set; }

        public bool IsBusinessStoreSettingsCompleted { get; set; }

        public bool IsLaundryPricesCompleted { get; set; }

        public bool IsBarcodeTagsCompleted { get; set; }

        public bool IsFirstCustomerCompleted { get; set; }

        public bool IsFirstOrderCompleted { get; set; }

        public bool IsTrialExpired => TrialDaysRemaining.HasValue && TrialDaysRemaining.Value < 0;
    }
}
