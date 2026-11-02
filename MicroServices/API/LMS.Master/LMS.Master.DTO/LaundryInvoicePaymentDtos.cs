namespace LMS.Master.DTO
{
    public class CreateLaundryInvoicePaymentOrderRequestDto
    {
        public string OrderNo { get; set; }
        public string ClientRequestId { get; set; }
    }

    public class CreateLaundryInvoicePaymentOrderResponseDto
    {
        public int TransactionId { get; set; }
        public string GatewayOrderId { get; set; }
        public string RazorpayKeyId { get; set; }
        public long AmountInMinorUnits { get; set; }
        public string Currency { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyLocale { get; set; }
        public int CurrencyDecimalDigits { get; set; }
        public string InvoiceNo { get; set; }
        public string OrderNo { get; set; }
        public bool IsTestMode { get; set; }
    }

    public class ConfirmLaundryInvoicePaymentRequestDto
    {
        public int TransactionId { get; set; }
        public string RazorpayOrderId { get; set; }
        public string RazorpayPaymentId { get; set; }
        public string RazorpaySignature { get; set; }
    }
}
