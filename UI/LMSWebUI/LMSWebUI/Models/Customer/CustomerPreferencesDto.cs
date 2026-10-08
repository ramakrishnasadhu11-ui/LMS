namespace LMSWebUI.Models.Customer
{
    public class CustomerPreferencesDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public bool EnableSmsNotifications { get; set; }
        public bool EnableEmailNotifications { get; set; }
        public bool AutoGenerateCustomerCode { get; set; }
        public bool RequirePhoneNumber { get; set; }
        public bool RequireEmail { get; set; }
        public bool AllowDuplicatePhoneNumber { get; set; }
        public string DefaultServiceType { get; set; }
        public string DefaultPaymentMode { get; set; }
        public string DefaultStarchLevel { get; set; }
        public int PickupReminderHours { get; set; }
        public int LoyaltyPointsPerOrder { get; set; }
    }
}
