using System;
using System.ComponentModel.DataAnnotations;

namespace LMS.Identity.DTO.Entities
{
    public class TenantSubscriptionStateHistoryEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string TenantId { get; set; }

        [Required]
        public int TenantEntityId { get; set; }

        [MaxLength(32)]
        public string PreviousSubscriptionStatus { get; set; }

        [Required]
        [MaxLength(32)]
        public string NewSubscriptionStatus { get; set; }

        [MaxLength(32)]
        public string PreviousPlanCode { get; set; }

        [MaxLength(32)]
        public string NewPlanCode { get; set; }

        [MaxLength(256)]
        public string ChangedBy { get; set; }

        [MaxLength(256)]
        public string ChangeReason { get; set; }

        public DateTime ChangedAtUtc { get; set; }

        public int? TenantSubscriptionPaymentOrderId { get; set; }

        [MaxLength(200)]
        public string ActivationIdempotencyKey { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
