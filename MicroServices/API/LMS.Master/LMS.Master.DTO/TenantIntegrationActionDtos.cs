namespace LMS.Master.DTO
{
    public class TenantIntegrationToggleRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class TenantIntegrationTestRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
    }
}