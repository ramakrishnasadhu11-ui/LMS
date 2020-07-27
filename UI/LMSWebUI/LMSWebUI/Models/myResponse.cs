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
    }
}
