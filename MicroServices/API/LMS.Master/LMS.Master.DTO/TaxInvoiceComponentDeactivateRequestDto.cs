namespace LMS.Master.DTO
{
    public class TaxInvoiceComponentDeactivateRequestDto
    {
        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public int ComponentId { get; set; }

        public string ConcurrencyToken { get; set; }
    }
}
