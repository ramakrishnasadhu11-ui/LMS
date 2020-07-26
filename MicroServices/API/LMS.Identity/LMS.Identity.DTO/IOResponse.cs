using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace LMS.Identity.DTO
{
    /// <summary>
    /// IOResponse Model
    /// </summary>
  
    public class IOResponse
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
       
         [StringLength(maximumLength: int.MaxValue)]
        public string TenantName { get; set; }
       
    }
}
