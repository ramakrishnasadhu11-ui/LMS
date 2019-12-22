using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;
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
        [JsonProperty("customer")]
        public ICollection<Customer> Customer { get; set; }

        public static UserGenderDto FromModel(UserGender model)
        {
            return new UserGenderDto()
            {
                UserGenderId = model.UserGenderId, 
                GenderName = model.GenderName, 
                Customer = model.Customer, 
            }; 
        }

        public UserGender ToModel()
        {
            return new UserGender()
            {
                UserGenderId = UserGenderId, 
                GenderName = GenderName, 
                Customer = Customer, 
            }; 
        }
    }
}