using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.DTO.Entities
{
    public class TenantStoreInfoEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

       [BsonElement]
        public string TenantId { get; set; }
        [BsonElement]
        public string Name { get; set; }


       [BsonElement]
        public string Password { get; set; }

        [BsonElement]
        public string Status {get;set;}

        [BsonElement]
        public bool IsPasswordChanged {get;set;}

        [BsonElement]
        public List<string> Storecodes {get;set;}
    }
}
