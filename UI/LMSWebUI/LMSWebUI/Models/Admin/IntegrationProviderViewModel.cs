using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Admin
{
    public class IntegrationProviderViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public string Category { get; set; }
        public string ProviderKey { get; set; }
        public string ProviderDisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsTestMode { get; set; }
        public Dictionary<string, string> PublicSettings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SecretSettings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> SecretKeys { get; set; } = new List<string>();
        public bool HasSecretsConfigured { get; set; }
        public string LastTestStatus { get; set; }
        public string LastTestMessage { get; set; }
        public DateTime? LastTestedAtUtc { get; set; }
        public bool ClearSecrets { get; set; }
    }
}