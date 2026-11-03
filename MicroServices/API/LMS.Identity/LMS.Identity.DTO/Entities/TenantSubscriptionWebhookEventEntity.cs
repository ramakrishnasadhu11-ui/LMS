using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantSubscriptionWebhookEventEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        public int TenantEntityId { get; set; }

        public int? TenantSubscriptionPaymentOrderId { get; set; }

        [MaxLength(128)]
        public string EventId { get; set; }

        [Required]
        [MaxLength(32)]
        public string PaymentProvider { get; set; }

        public bool IsLiveMode { get; set; }

        [Required]
        [MaxLength(128)]
        public string EventType { get; set; }

        [Required]
        public string PayloadJson { get; set; }

        [Required]
        [MaxLength(32)]
        public string ProcessingStatus { get; set; }

        [MaxLength(1024)]
        public string ProcessingError { get; set; }

        public DateTime ReceivedAtUtc { get; set; }

        public DateTime? ProcessedAtUtc { get; set; }

        public bool IsSignatureVerified { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
