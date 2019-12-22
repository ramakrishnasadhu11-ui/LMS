using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Master.DataModels.Entities;

namespace LMS.Master.DTO.Entities.Dto
{
    [DataContract]
    public class AvailableLaundryCustomerTypesForClientDto
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
        [JsonProperty("customerTypeId")]
        public int CustomerTypeId { get; set; }

        [DataMember]
        [JsonProperty("customerTypeName")]
        public string CustomerTypeName { get; set; }

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

        public static AvailableLaundryCustomerTypesForClientDto FromModel(AvailableLaundryCustomerTypesForClient model)
        {
            return new AvailableLaundryCustomerTypesForClientDto()
            {
                Id = model.Id, 
                ClientId = model.ClientId, 
                ClientStoreCode = model.ClientStoreCode, 
                CustomerTypeId = model.CustomerTypeId, 
                CustomerTypeName = model.CustomerTypeName, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public AvailableLaundryCustomerTypesForClient ToModel()
        {
            return new AvailableLaundryCustomerTypesForClient()
            {
                Id = Id, 
                ClientId = ClientId, 
                ClientStoreCode = ClientStoreCode, 
                CustomerTypeId = CustomerTypeId, 
                CustomerTypeName = CustomerTypeName, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}