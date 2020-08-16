using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Master.DTO.Entity
{
    class CustomerEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonElement]
        public string CustomerName { get; set; }
        [BsonElement]
        public string Address { get; set; }
        [BsonElement]
        public string StoreCode {get;set;}
        [BsonElement]
        public string TenantName {get;set;}
        [BsonElement]
        public string CustCode {get;set;}
    }
}
