using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.EntityFramework.Entities;
using System.Collections.Generic;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class UserGenderDto
    {
        [DataMember]
        [JsonProperty("userGenderId")]
        public int UserGenderId { get; set; }

        [DataMember]
        [JsonProperty("genderName")]
        public string GenderName { get; set; }

        [DataMember]
        [JsonProperty("client")]
        public ICollection<Client> Client { get; set; }

        public static UserGenderDto FromModel(UserGender model)
        {
            return new UserGenderDto()
            {
                UserGenderId = model.UserGenderId, 
                GenderName = model.GenderName, 
                Client = model.Client, 
            }; 
        }

        public UserGender ToModel()
        {
            return new UserGender()
            {
                UserGenderId = UserGenderId, 
                GenderName = GenderName, 
                Client = Client, 
            }; 
        }
    }
}