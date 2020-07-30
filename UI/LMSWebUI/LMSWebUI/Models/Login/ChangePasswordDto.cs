using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Login
{
    public class ChangePasswordDto
    {
        public string NewPassword { get; set; }
        public string Email {get;set;}
        public string OldPassword { get; set; }
      
    }
}
