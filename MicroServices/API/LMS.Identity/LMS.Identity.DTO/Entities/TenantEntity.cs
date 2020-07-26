using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO.Entities
{
    public class TenantEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

       [BsonElement]
        public int NoOfStores { get; set; }
       
        [BsonElement]
        public string TenantId { get; set; }
        
       [BsonElement]
        public string TenantName { get; set; }

        [BsonElement]
        public string Email {get;set;}

       [BsonElement]
       public int PhoneNumber { get; set; }
      
       [BsonElement]
       public string MiddleName { get; set; }

       [BsonElement]
       public string FamilyName { get; set; }

       [BsonElement]
       public string Address { get; set; }
        
       [BsonElement]
       public string City { get; set; }

       [BsonElement]
       public string Region { get; set; }

       [BsonElement]
       public string Zip { get; set; }

       [BsonElement]
       public string Country { get; set; }

       [BsonElement]
       public DateTime CreatedDate { get; set; }
        
       [BsonElement]
       public DateTime ModifiedDate { get; set; }

    }
}
