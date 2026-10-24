using System;
using LMSWebUI.Models.DashBoard;

namespace LMSWebUI.Models.Login
{
    public class TenantProfileViewModel : TenantInfoDto
    {
        public string Email { get; set; }

        public string PhoneNumber { get; set; }

        public string Address { get; set; }

        public string Country { get; set; }

        public string UserRole { get; set; }

        public DateTime? CreatedDate { get; set; }

        public string ProfileImageUrl { get; set; }
    }
}
