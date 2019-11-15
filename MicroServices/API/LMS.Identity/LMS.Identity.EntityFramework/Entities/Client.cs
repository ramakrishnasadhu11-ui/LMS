using System;
using System.Collections.Generic;

namespace LMS.Identity.EntityFramework.Entities
{
    public partial class Client
    {
        public int ClientId { get; set; }
        public int ClientGenderId { get; set; }
        public string ClientName { get; set; }
        public int PhoneNumber { get; set; }
        public string Email { get; set; }
        public string MiddleName { get; set; }
        public string FamilyName { get; set; }
        public byte[] Photo { get; set; }
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string Region { get; set; }
        public string Zip { get; set; }
        public string Country { get; set; }
        public bool? Active { get; set; }
        public int? CreatedByUserId { get; set; }
        public DateTime CreatedDate { get; set; }
        public int? ModifiedByUserId { get; set; }
        public DateTime ModifiedDate { get; set; }

        public virtual UserGender ClientGender { get; set; }
    }
}