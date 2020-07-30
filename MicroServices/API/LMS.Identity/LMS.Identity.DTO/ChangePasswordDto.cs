using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO
{
    public class ChangePasswordDto
    {
         public string NewPassword { get; set; }
        public string Email {get;set;}
       public string OldPassword { get; set; }
      
    }
}
