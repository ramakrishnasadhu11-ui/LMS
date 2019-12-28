using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Login
{
    public class ClientLoginDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string StoreCode { get; set; }
        public string Message { get; set; }
        public bool ShowDialog { get; set; }
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }
}
