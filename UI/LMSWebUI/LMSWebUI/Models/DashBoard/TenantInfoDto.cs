using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LMSWebUI.Models.DashBoard
{
    public class TenantInfoDto
    {
        public string TenantName {get;set;}
        public string TenantStore {get;set;}
        public string StoreName { get; set; }
        public bool IsStoreActive { get; set; }
        public List<DashboardStoreOption> ActiveStores { get; set; } = new List<DashboardStoreOption>();
        public DateTime? TrialEndUtc { get; set; }
        public int? TrialDaysRemaining { get; set; }
        public string SubscriptionStatus { get; set; }
    }

    public class DashboardStoreOption
    {
        public string StoreName { get; set; }
        public string StoreCode { get; set; }
        public bool IsActive { get; set; }
    }
}
