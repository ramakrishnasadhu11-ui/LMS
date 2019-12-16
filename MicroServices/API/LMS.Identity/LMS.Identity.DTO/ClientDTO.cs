using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class ClientDto
    {
        [DataMember]
        [JsonProperty("clientId")]
        public int ClientId { get; set; }

        [DataMember]
        [JsonProperty("firstName")]
        public string FirstName { get; set; }

        [DataMember]
        [JsonProperty("lastName")]
        public string LastName { get; set; }

        [DataMember]
        [JsonProperty("companyName")]
        public string CompanyName { get; set; }

        [DataMember]
        [JsonProperty("email")]
        public string Email { get; set; }

        [DataMember]
        [JsonProperty("phoneNumber")]
        public int PhoneNumber { get; set; }

        [DataMember]
        [JsonProperty("photo")]
        public byte[] Photo { get; set; }

        [DataMember]
        [JsonProperty("numberOfStores")]
        public int NumberOfStores { get; set; }

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

        public static ClientDto FromModel(Client model)
        {
            return new ClientDto()
            {
                ClientId = model.ClientId, 
                FirstName = model.FirstName, 
                LastName = model.LastName, 
                CompanyName = model.CompanyName, 
                Email = model.Email, 
                PhoneNumber = model.PhoneNumber, 
                Photo = model.Photo.ToArray(), 
                NumberOfStores = model.NumberOfStores, 
                Active = model.Active, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
            }; 
        }

        public Client ToModel()
        {
            return new Client()
            {
                ClientId = ClientId, 
                FirstName = FirstName, 
                LastName = LastName, 
                CompanyName = CompanyName, 
                Email = Email, 
                PhoneNumber = PhoneNumber, 
                Photo = Photo.ToArray(), 
                NumberOfStores = NumberOfStores, 
                Active = Active, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
            }; 
        }
    }
}