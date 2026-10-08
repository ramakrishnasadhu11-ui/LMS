using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantStoreInfoEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantId { get; set; }

        public string Name { get; set; }

        public string Password { get; set; }

        public string Status {get;set;}

        public bool IsPasswordChanged {get;set;}

        public List<string> Storecodes {get;set;}
    }
}
