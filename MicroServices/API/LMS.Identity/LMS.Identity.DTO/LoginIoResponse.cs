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
        ///  Guid
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string TenantId { get; set; }
    }
}
