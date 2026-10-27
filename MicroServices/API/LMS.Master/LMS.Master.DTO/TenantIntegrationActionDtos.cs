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

    public class TenantIntegrationSendTestEmailRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
        public string RecipientEmail { get; set; }
    }

    public class TenantIntegrationOrderBillEmailRequestDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
        public string RecipientEmail { get; set; }
        public string Subject { get; set; }
        public string TextBody { get; set; }
        public string HtmlBody { get; set; }
        public string AttachmentFileName { get; set; }
        public string AttachmentContentBase64 { get; set; }
    }
}