using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using VMD.RESTApiResponseWrapper.Core.Wrappers;

namespace LMSWebUI.Models
{
    public class myResponse
    {

        [DataMember]
        public string Version { get; set; }
        [DataMember]
        public int StatusCode { get; set; }
        [DataMember]
        public string Message { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public ApiError ResponseException { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public object Result { get; set; }
    }
}
