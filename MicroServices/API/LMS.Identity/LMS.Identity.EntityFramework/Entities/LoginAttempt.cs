using System;
using System.Collections.Generic;

namespace LMS.Identity.EntityFramework.Entities
{
    public partial class LoginAttempt
    {
        public int LoginAttemptId { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Ipnumber { get; set; }
        public string BrowserType { get; set; }
        public bool? Success { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
}