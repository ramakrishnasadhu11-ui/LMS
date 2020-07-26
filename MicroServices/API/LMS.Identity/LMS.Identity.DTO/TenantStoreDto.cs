using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO
{
    public class TenantStoreDto
    {
        public string Id { get; set; }
        public string TenantId { get; set; }
        public string Password { get; set; }
        public string Status {get;set;}
        public bool IsPasswordChanged {get;set;}
    }
}
