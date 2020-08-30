using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Customer
{
    public class CustomerInfoDto
    {
        public string CustomerName { get; set; }
        public string SearchCustomer {get;set;}
       public string Address { get; set; }
        public string StoreCode {get;set;}
        public string TenantName {get;set;}
        public bool disableStatus {get;set;}
        public string customerCode {get;set;}
       public string MembershipId {get;set;}
        public string BarCode {get;set;}
      
    }
}
