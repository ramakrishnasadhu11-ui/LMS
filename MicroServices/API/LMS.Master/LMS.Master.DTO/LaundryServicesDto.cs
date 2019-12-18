using LMS.Master.DataModels.Entities;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;

namespace LMS.Master.DTO.Entities.Dto
{
    [DataContract]
    public class LaundryServicesDto
    {
        [DataMember]
        [JsonProperty("clientId")]
        public int ClientId { get; set; }

        [DataMember]
        [JsonProperty("clientStoreCode")]
        public string ClientStoreCode { get; set; }

        [DataMember]
        [JsonProperty("serviceCode")]
        public string ServiceCode { get; set; }

        [DataMember]
        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [DataMember]
        [JsonProperty("createdByUserId")]
        public int CreatedByUserId { get; set; }

        [DataMember]
        [JsonProperty("modifiedByUserId")]
        public int ModifiedByUserId { get; set; }
    }
}