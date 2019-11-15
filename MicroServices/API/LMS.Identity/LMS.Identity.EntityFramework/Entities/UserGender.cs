using System;
using System.Collections.Generic;

namespace LMS.Identity.EntityFramework.Entities
{
    public partial class UserGender
    {
        public UserGender()
        {
            Client = new HashSet<Client>();
        }

        public int UserGenderId { get; set; }
        public string GenderName { get; set; }

        public virtual ICollection<Client> Client { get; set; }
    }
}