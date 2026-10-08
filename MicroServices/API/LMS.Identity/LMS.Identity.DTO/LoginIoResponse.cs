using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace LMS.Identity.DTO
{
    public class LoginIoResponse
    {
         /// <summary>
        ///  Message
        /// </summary>
        
        [StringLength(maximumLength: int.MaxValue)]
        public string Message { get; set; }

        /// <summary>
        ///  TenantId
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string TenantId { get; set; }

         /// <summary>
        ///  TenantName
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
      
         public string TenantName { get; set; }

        /// <summary>
        ///  StatusCode
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
         public string StatusCode { get; set; }

        /// <summary>
        ///  StoreCodea
        /// </summary>
        public List<string> Storecodes { get; set; }

        public string UserRole { get; set; }

        /// <summary>
        /// Indicates that the caller must force the user to change their password (first-login)
        /// </summary>
        public bool? MustChangePassword { get; set; }
    }
}
