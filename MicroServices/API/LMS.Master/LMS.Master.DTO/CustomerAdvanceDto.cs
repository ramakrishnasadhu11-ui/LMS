using System;

namespace LMS.Master.DTO
{
    public class CustomerAdvanceDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CustCode { get; set; }

        public decimal AdvanceAmount { get; set; }

        public string TransactionType { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}
