using System;
using System.Collections.Generic;
using System.Linq;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    public class UserGenderDto
    {
        public int UserGenderId { get; set; }

        public string GenderName { get; set; }

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