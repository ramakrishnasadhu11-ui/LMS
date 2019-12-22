using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO
{
    public class ClientLoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string StoreCode { get; set; }
    }
}
