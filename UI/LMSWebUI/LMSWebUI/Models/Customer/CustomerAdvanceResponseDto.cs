using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class CustomerAdvanceResponseDto
    {
        public string StatusCode { get; set; }

        public string CustCode { get; set; }

        public decimal Balance { get; set; }

        public List<CustomerAdvanceDto> Transactions { get; set; } = new List<CustomerAdvanceDto>();
    }
}
