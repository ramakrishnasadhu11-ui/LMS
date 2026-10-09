using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using VMD.RESTApiResponseWrapper.Core.Wrappers;

namespace LMSWebUI.Models
{
    public class myResponse
    {

        [DataMember]
        public HttpStatusCode StatusCode { get; set; }
        [DataMember]
        public object ResultSet { get; set; }
   }
     public class ActionReturnType
    {
        public HttpStatusCode StatusCode { get; set; }
        public string XTotalCount { get; set; }
        public object ResultSet { get; set; }
    }
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

         /// <summary>
        ///  PhoneNumber
        /// </summary>
        public string PhoneNumber {get;set;}

        /// <summary>
        ///  Address
        /// </summary>
         public string Address {get;set;}


         public string BarCode { get; set; }

        public string Email { get; set; }

        /// <summary>
        ///  Country
        /// </summary>
         public string Country {get;set;}

         /// <summary>
         /// Tenant membership start date
         /// </summary>
         public DateTime? CreatedDate { get; set; }

        public string UserRole { get; set; }

        /// <summary>
        /// Indicates that the caller must force the user to change their password (first-login)
        /// </summary>
        public bool? MustChangePassword { get; set; }

   
    }
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
        public string PhoneNumber {get;set;}

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
