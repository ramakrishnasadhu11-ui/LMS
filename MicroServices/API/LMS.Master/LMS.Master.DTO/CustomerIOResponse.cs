using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace LMS.Master.DTO
{
    /// <summary>
    /// IOResponse Model
    /// </summary>
  
    public class CustomerIOResponse
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
        public string CustCode { get; set; }

        [StringLength(maximumLength: int.MaxValue)]
        public string CustomerName { get; set; }
       
        /// <summary>
        ///  TenantName
        /// </summary>
       
        [StringLength(maximumLength: int.MaxValue)]
        public string TenantName { get; set; }
       
         /// <summary>
        ///  Email
        /// </summary>
       
        [StringLength(maximumLength: int.MaxValue)]
        public string Email { get; set; }

        /// <summary>
        ///  StatusCode
        /// </summary>
        [StringLength(maximumLength: int.MaxValue)]
        public string StatusCode { get; set; }

        /// <summary>
        ///  StoreCodea
        /// </summary>
        public List<string> Storecodes { get; set; }

        /// <summary>
        ///  PhoneNumber
        /// </summary>
        public string PhoneNumber { get; set; }

        /// <summary>
        ///  Address
        /// </summary>
         public string Address {get;set;}

        // MembershipId removed system-wide

        public string BarCode { get; set; }

        /// <summary>
        ///  Country
        /// </summary>
         public string Country {get;set;}

        /// <summary>
        ///  CustomerNames
        /// </summary>
         public List<string> CustomerNames {get;set;}


    }
}
