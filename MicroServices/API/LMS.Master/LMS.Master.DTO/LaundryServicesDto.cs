using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Master.DataModels.Entities;

namespace LMS.Master.DTO.Entities.Dto
{
    [DataContract]
    public class LaundryServicesDto
    {
        [DataMember]
        [JsonProperty("id")]
        public int Id { get; set; }

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
        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; }

        [DataMember]
        [JsonProperty("modifiedByUserId")]
        public int ModifiedByUserId { get; set; }

        [DataMember]
        [JsonProperty("modifiedDate")]
        public DateTime ModifiedDate { get; set; }

        public static LaundryServicesDto FromModel(LaundryServices model)
        {
            return new LaundryServicesDto()
            {
                Id = model.Id, 
                ClientId = model.ClientId, 
                ClientStoreCode = model.ClientStoreCode, 
                ServiceCode = model.ServiceCode, 
                ServiceName = model.ServiceName, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public LaundryServices ToModel()
        {
            return new LaundryServices()
            {
                Id = Id, 
                ClientId = ClientId, 
                ClientStoreCode = ClientStoreCode, 
                ServiceCode = ServiceCode, 
                ServiceName = ServiceName, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}