using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantSubscriptionPaymentTransactionEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenantSubscriptionPaymentOrderId { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        public int TenantEntityId { get; set; }

        [MaxLength(128)]
        public string GatewayPaymentId { get; set; }

        [MaxLength(128)]
        public string GatewayOrderId { get; set; }

        [Required]
        [MaxLength(32)]
        public string PaymentProvider { get; set; }

        public bool IsLiveMode { get; set; }

        [Required]
        [MaxLength(32)]
        public string TransactionStatus { get; set; }

        public decimal Amount { get; set; }

        [Required]
        [MaxLength(8)]
        public string Currency { get; set; }

        public DateTime CreatedDateUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public DateTime? VerifiedAtUtc { get; set; }

        public bool IsSignatureVerified { get; set; }

        [MaxLength(64)]
        public string FailureCode { get; set; }

        [MaxLength(512)]
        public string FailureReason { get; set; }

        public DateTime? SubscriptionActivationEmailSentAtUtc { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
