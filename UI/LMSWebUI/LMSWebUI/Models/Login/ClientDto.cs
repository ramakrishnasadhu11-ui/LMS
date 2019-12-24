using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace LMSWebUI.Models.Login
{
    public class ClientDto
    {
        [DataMember]
        [JsonProperty("id")]
        public int Id { get; set; }

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
        public int? NumberOfStores { get; set; }

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

        public string Message { get; set; }

    }
}
