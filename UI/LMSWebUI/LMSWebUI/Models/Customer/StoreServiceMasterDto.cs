using System;

namespace LMSWebUI.Models.Customer
{
    public class StoreServiceMasterDto
    {
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string MasterType { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }
    }
}
