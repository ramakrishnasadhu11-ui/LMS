using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantSubscriptionPaymentOrderEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        public int TenantEntityId { get; set; }

        [Required]
        [MaxLength(32)]
        public string PlanCode { get; set; }

        [Required]
        [MaxLength(16)]
        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        [Required]
        [MaxLength(8)]
        public string Currency { get; set; }

        [Required]
        [MaxLength(32)]
        public string OrderStatus { get; set; }

        [MaxLength(128)]
        public string GatewayOrderId { get; set; }

        [MaxLength(128)]
        public string ReceiptId { get; set; }

        public DateTime CreatedDateUtc { get; set; }

        public DateTime? ModifiedDateUtc { get; set; }

        public DateTime? OrderExpiresUtc { get; set; }

        [MaxLength(32)]
        public string PendingVerificationStatus { get; set; }

        public DateTime? PendingVerificationUntilUtc { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
