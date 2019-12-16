using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using LMS.Identity.DataModels.Entities;

namespace LMS.Identity.DTO.Entities.Dto
{
    [DataContract]
    public class CustomerDto
    {
        [DataMember]
        [JsonProperty("customerId")]
        public int CustomerId { get; set; }

        [DataMember]
        [JsonProperty("customerGenderId")]
        public int CustomerGenderId { get; set; }

        [DataMember]
        [JsonProperty("customerName")]
        public string CustomerName { get; set; }

        [DataMember]
        [JsonProperty("phoneNumber")]
        public int PhoneNumber { get; set; }

        [DataMember]
        [JsonProperty("email")]
        public string Email { get; set; }

        [DataMember]
        [JsonProperty("middleName")]
        public string MiddleName { get; set; }

        [DataMember]
        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [DataMember]
        [JsonProperty("photo")]
        public byte[] Photo { get; set; }

        [DataMember]
        [JsonProperty("address1")]
        public string Address1 { get; set; }

        [DataMember]
        [JsonProperty("address2")]
        public string Address2 { get; set; }

        [DataMember]
        [JsonProperty("city")]
        public string City { get; set; }

        [DataMember]
        [JsonProperty("region")]
        public string Region { get; set; }

        [DataMember]
        [JsonProperty("zip")]
        public string Zip { get; set; }

        [DataMember]
        [JsonProperty("country")]
        public string Country { get; set; }

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

        [DataMember]
        [JsonProperty("customerGender")]
        public UserGenderDto CustomerGender { get; set; }

        public static CustomerDto FromModel(Customer model)
        {
            return new CustomerDto()
            {
                CustomerId = model.CustomerId, 
                CustomerGenderId = model.CustomerGenderId, 
                CustomerName = model.CustomerName, 
                PhoneNumber = model.PhoneNumber, 
                Email = model.Email, 
                MiddleName = model.MiddleName, 
                FamilyName = model.FamilyName, 
                Photo = model.Photo.ToArray(), 
                Address1 = model.Address1, 
                Address2 = model.Address2, 
                City = model.City, 
                Region = model.Region, 
                Zip = model.Zip, 
                Country = model.Country, 
                Active = model.Active, 
                CreatedByUserId = model.CreatedByUserId, 
                CreatedDate = model.CreatedDate, 
                ModifiedByUserId = model.ModifiedByUserId, 
                ModifiedDate = model.ModifiedDate, 
                CustomerGender = UserGenderDto.FromModel(model.CustomerGender), 
            }; 
        }

        public Customer ToModel()
        {
            return new Customer()
            {
                CustomerId = CustomerId, 
                CustomerGenderId = CustomerGenderId, 
                CustomerName = CustomerName, 
                PhoneNumber = PhoneNumber, 
                Email = Email, 
                MiddleName = MiddleName, 
                FamilyName = FamilyName, 
                Photo = Photo.ToArray(), 
                Address1 = Address1, 
                Address2 = Address2, 
                City = City, 
                Region = Region, 
                Zip = Zip, 
                Country = Country, 
                Active = Active, 
                CreatedByUserId = CreatedByUserId, 
                CreatedDate = CreatedDate, 
                ModifiedByUserId = ModifiedByUserId, 
                ModifiedDate = ModifiedDate, 
                CustomerGender = CustomerGender.ToModel(), 
            }; 
        }
    }
}