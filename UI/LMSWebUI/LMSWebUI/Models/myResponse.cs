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
        public int PhoneNumber {get;set;}

        /// <summary>
        ///  Address
        /// </summary>
         public string Address {get;set;}

        /// <summary>
        ///  Country
        /// </summary>
         public string Country {get;set;}

   
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
        public int PhoneNumber {get;set;}

        /// <summary>
        ///  Address
        /// </summary>
         public string Address {get;set;}

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
