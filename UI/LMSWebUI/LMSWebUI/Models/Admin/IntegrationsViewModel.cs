using System;
using System.Collections.Generic;
using System.Linq;

namespace LMSWebUI.Models.Admin
{
    public class IntegrationsViewModel
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public List<IntegrationProviderViewModel> Providers { get; set; } = new List<IntegrationProviderViewModel>();

        public IEnumerable<IGrouping<string, IntegrationProviderViewModel>> ProvidersByCategory
            => (Providers ?? new List<IntegrationProviderViewModel>())
                .GroupBy(x => x.Category ?? "Other");
    }
}