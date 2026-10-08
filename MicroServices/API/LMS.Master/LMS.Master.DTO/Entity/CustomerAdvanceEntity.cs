using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class CustomerAdvanceEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CustCode { get; set; }

        public decimal AdvanceAmount { get; set; }

        public string TransactionType { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}
