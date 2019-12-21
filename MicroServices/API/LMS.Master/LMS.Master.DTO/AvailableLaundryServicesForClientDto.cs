using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Master.DataModels.Entities;

namespace LMS.Master.DTO.Entities.Dto
{
    [DataContract]
    public class AvailableLaundryServicesForClientDto
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
        [JsonProperty("customerServiceTypeId")]
        public int CustomerServiceTypeId { get; set; }

        [DataMember]
        [JsonProperty("customerServiceType")]
        public string CustomerServiceType { get; set; }

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

        public static AvailableLaundryServicesForClientDto FromModel(AvailableLaundryServicesForClient model)
        {
            return new AvailableLaundryServicesForClientDto()
            {
                Id = model.Id, 
                ClientId = model.ClientId, 
                ClientStoreCode = model.ClientStoreCode, 
                CustomerServiceTypeId = model.CustomerServiceTypeId, 
                CustomerServiceType = model.CustomerServiceType, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public AvailableLaundryServicesForClient ToModel()
        {
            return new AvailableLaundryServicesForClient()
            {
                Id = Id, 
                ClientId = ClientId, 
                ClientStoreCode = ClientStoreCode, 
                CustomerServiceTypeId = CustomerServiceTypeId, 
                CustomerServiceType = CustomerServiceType, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}