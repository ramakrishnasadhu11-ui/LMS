using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class CustomerAdvanceDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string CustCode { get; set; }

        public string CustomerName { get; set; }

        public decimal AdvanceAmount { get; set; }

        public string TransactionType { get; set; } = "Credit";

        public string Notes { get; set; }

        public DateTime CreatedDate { get; set; }

        public decimal Balance { get; set; }

        public List<CustomerAdvanceDto> Transactions { get; set; } = new List<CustomerAdvanceDto>();
    }
}
