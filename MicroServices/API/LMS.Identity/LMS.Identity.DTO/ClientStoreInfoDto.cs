using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class ClientStoreInfoDto
    {
        [DataMember]
        [JsonProperty("id")]
        public int Id { get; set; }

        [DataMember]
        [JsonProperty("clientId")]
        public int? ClientId { get; set; }

        [DataMember]
        [JsonProperty("clientStoreCode")]
        public string ClientStoreCode { get; set; }

        [DataMember]
        [JsonProperty("createdByUserId")]
        public int? CreatedByUserId { get; set; }

        [DataMember]
        [JsonProperty("createdDate")]
        public DateTime? CreatedDate { get; set; }

        [DataMember]
        [JsonProperty("modifiedByUserId")]
        public int? ModifiedByUserId { get; set; }

        [DataMember]
        [JsonProperty("modifiedDate")]
        public DateTime? ModifiedDate { get; set; }

        public static ClientStoreInfoDto FromModel(ClientStoreInfo model)
        {
            return new ClientStoreInfoDto()
            {
                Id = model.Id, 
                ClientId = model.ClientId, 
                ClientStoreCode = model.ClientStoreCode, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public ClientStoreInfo ToModel()
        {
            return new ClientStoreInfo()
            {
                Id = Id, 
                ClientId = ClientId, 
                ClientStoreCode = ClientStoreCode, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}