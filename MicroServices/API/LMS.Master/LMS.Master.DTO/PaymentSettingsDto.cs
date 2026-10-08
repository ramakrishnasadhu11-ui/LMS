namespace LMS.Master.DTO
{
    public class PaymentSettingsDto
    {
        public string TenantName { get; set; }
        public string StoreCode { get; set; }
        public bool EnableCash { get; set; }
        public bool EnableUpi { get; set; }
        public bool EnableCard { get; set; }
        public bool EnableWallet { get; set; }
        public string DefaultPaymentMode { get; set; }
        public bool AllowPartialPayment { get; set; }
        public bool AllowCredit { get; set; }
        public decimal CreditLimitAmount { get; set; }
        public bool RoundOffPayableAmount { get; set; }
        public string Notes { get; set; }
    }
}
