using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class ClientPasswordDto
    {
        [DataMember]
        [JsonProperty("id")]
        public int Id { get; set; }

        [DataMember]
        [JsonProperty("clientId")]
        public int? ClientId { get; set; }

        [DataMember]
        [JsonProperty("password")]
        public string Password { get; set; }

        [DataMember]
        [JsonProperty("active")]
        public bool? Active { get; set; }

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

        public static ClientPasswordDto FromModel(ClientPassword model)
        {
            return new ClientPasswordDto()
            {
                Id = model.Id, 
                ClientId = model.ClientId, 
                Password = model.Password, 
                Active = model.Active, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public ClientPassword ToModel()
        {
            return new ClientPassword()
            {
                Id = Id, 
                ClientId = ClientId, 
                Password = Password, 
                Active = Active, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}