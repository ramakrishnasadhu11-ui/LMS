using System;
using System.Linq;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    public class PasswordDto
    {
        public int PasswordId { get; set; }

        public int? ClientId { get; set; }

        public string Password1 { get; set; }

        public string PasswordAnswer { get; set; }

        public string PasswordQuestion { get; set; }

        public bool? Active { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedDate { get; set; }

        public int? ModifiedByUserId { get; set; }

        public DateTime ModifiedDate { get; set; }

        public static PasswordDto FromModel(Password model)
        {
            return new PasswordDto()
            {
                PasswordId = model.PasswordId, 
                ClientId = model.ClientId, 
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
                ClientId = ClientId, 
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