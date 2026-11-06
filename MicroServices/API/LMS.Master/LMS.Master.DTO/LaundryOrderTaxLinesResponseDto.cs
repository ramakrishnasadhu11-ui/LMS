using System.Collections.Generic;

namespace LMS.Master.DTO
{
    public class LaundryOrderTaxLinesResponseDto
    {
        public string StatusCode { get; set; }

        public string OrderNo { get; set; }

        public List<LaundryOrderTaxLineDto> TaxLines { get; set; } = new List<LaundryOrderTaxLineDto>();
    }
}
