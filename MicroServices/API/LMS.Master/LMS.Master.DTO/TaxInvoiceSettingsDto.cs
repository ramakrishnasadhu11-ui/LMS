namespace LMS.Master.DTO
{
    public class TaxInvoiceSettingsDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public bool EnableTax { get; set; }
        public decimal GstPercent { get; set; }
        public bool PricesIncludeTax { get; set; }
        public string InvoicePrefix { get; set; }
        public int NextInvoiceNumber { get; set; }
        public int InvoiceNumberPadding { get; set; }
        public bool ResetInvoiceNumberYearly { get; set; }
        public string Notes { get; set; }
    }
}
