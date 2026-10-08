using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class CustomerEntity
    {
        [Key]
        public int Id { get; set; }

        public string CustomerName { get; set; }

        public string Address { get; set; }


        public string BarCode { get; set; }

        public string PhoneNumber { get; set; }

        public string Email { get; set; }

        public string StoreCode {get;set;}

        public string TenantName {get;set;}

        public string CustCode {get;set;}

        public DateTime CreatedDate {get;set;}

        public DateTime ModifiedDate {get;set;}

    }
}
