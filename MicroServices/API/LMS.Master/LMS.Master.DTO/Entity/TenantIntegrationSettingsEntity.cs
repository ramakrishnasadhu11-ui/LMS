using System;

namespace LMS.Master.DTO.Entity
{
    public class TenantIntegrationSettingsEntity
    {
        public int Id { get; set; }
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
        public string ProviderDisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsTestMode { get; set; }
        public string EncryptedSettingsJson { get; set; }
        public bool HasSecretsConfigured { get; set; }
        public string LastTestStatus { get; set; }
        public string LastTestMessage { get; set; }
        public DateTime? LastTestedAtUtc { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}