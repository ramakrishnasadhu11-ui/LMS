using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.EntityFramework.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class PasswordDto
    {
        [DataMember]
        [JsonProperty("passwordId")]
        public int PasswordId { get; set; }

        [DataMember]
        [JsonProperty("userId")]
        public int? UserId { get; set; }

        [DataMember]
        [JsonProperty("password1")]
        public string Password1 { get; set; }

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

        public static PasswordDto FromModel(Password model)
        {
            return new PasswordDto()
            {
                PasswordId = model.PasswordId, 
                UserId = model.UserId, 
                Password1 = model.Password1, 
                PasswordAnswer = model.PasswordAnswer, 
                PasswordQuestion = model.PasswordQuestion, 
                Active = model.Active, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public Password ToModel()
        {
            return new Password()
            {
                PasswordId = PasswordId, 
                UserId = UserId, 
                Password1 = Password1, 
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