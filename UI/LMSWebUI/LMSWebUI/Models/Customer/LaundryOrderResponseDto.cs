using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class LaundryOrderResponseDto
    {
        public string StatusCode { get; set; }

        public string CustCode { get; set; }

        public List<LaundryOrderDto> Orders { get; set; } = new List<LaundryOrderDto>();
    }
}
