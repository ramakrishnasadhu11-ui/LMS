using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO
{
    public class TenantDto
    {
       public string Id { get; set; }
       public int NoOfStores { get; set; }
        public string TenantId {get;set;}
       public string TenantName { get; set; }
       public string Email {get;set;}
       public string PhoneNumber { get; set; }
       public string MiddleName { get; set; }
       public string FamilyName { get; set; }
       public string Address { get; set; }
       public string City { get; set; }
       public string Region { get; set; }
       public string Zip { get; set; }
       public string Country { get; set; }
       public DateTime CreatedDate { get; set; }
    }
}
