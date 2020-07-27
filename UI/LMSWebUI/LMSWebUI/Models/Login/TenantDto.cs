using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Login
{
    public class TenantDto
    {
       public string NoOfStores { get; set; }
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
    }
}
