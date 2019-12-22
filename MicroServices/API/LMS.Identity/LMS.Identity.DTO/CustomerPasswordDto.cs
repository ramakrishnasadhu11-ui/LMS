using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class CustomerPasswordDto
    {
        [DataMember]
        [JsonProperty("passwordId")]
        public int PasswordId { get; set; }

        [DataMember]
        [JsonProperty("clientId")]
        public int? ClientId { get; set; }

        [DataMember]
        [JsonProperty("password")]
        public string Password { get; set; }

        [DataMember]
        [JsonProperty("passwordAnswer")]
        public string PasswordAnswer { get; set; }

        [DataMember]
        [JsonProperty("passwordQuestion")]
        public string PasswordQuestion { get; set; }

        [DataMember]
        [JsonProperty("active")]
        public bool? Active { get; set; }

        [DataMember]
        [JsonProperty("createdByUserId")]
        public int? CreatedByUserId { get; set; }

        [DataMember]
        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; }

        [DataMember]
        [JsonProperty("modifiedByUserId")]
        public int? ModifiedByUserId { get; set; }

        [DataMember]
        [JsonProperty("modifiedDate")]
        public DateTime ModifiedDate { get; set; }

        public static CustomerPasswordDto FromModel(CustomerPassword model)
        {
            return new CustomerPasswordDto()
            {
                PasswordId = model.PasswordId, 
                ClientId = model.ClientId, 
                Password = model.Password, 
                PasswordAnswer = model.PasswordAnswer, 
                PasswordQuestion = model.PasswordQuestion, 
                Active = model.Active, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public CustomerPassword ToModel()
        {
            return new CustomerPassword()
            {
                PasswordId = PasswordId, 
                ClientId = ClientId, 
                Password = Password, 
                PasswordAnswer = PasswordAnswer, 
                PasswordQuestion = PasswordQuestion, 
                Active = Active, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}