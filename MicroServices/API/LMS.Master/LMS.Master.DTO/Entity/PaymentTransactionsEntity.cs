using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Master.DTO.Entity
{
    public class PaymentTransactionsEntity
    {
        [Key]
        public int Id { get; set; }

        public string TenantName { get; set; }

        public string StoreCode { get; set; }

        public string OrderNo { get; set; }

        public string InvoiceNo { get; set; }

        public string CustCode { get; set; }

        public string GatewayProvider { get; set; }

        public string ClientRequestId { get; set; }

        public string GatewayOrderId { get; set; }

        public string GatewayPaymentId { get; set; }

        public string GatewaySignature { get; set; }

        public string Currency { get; set; }

        public decimal AmountRequested { get; set; }

        public decimal AmountAuthorized { get; set; }

        public decimal AmountCaptured { get; set; }

        public string Status { get; set; }

        public string FailureCode { get; set; }

        public string FailureReason { get; set; }

        public bool IsLiveMode { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ModifiedDate { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
