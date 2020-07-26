using System;
namespace LMS.Identity.TenantConfig
{
   public class TenantRegistryOptions
    {
         public const string TenantRegistryConnection = "TenantRegistryConnection";
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string CollectionName { get; set; }
        public int ConnectTimeoutInSeconds { get; set; }
    }
}
