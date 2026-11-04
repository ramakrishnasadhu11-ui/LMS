using System;
using System.Collections.Generic;

namespace LMS.Identity.DTO
{
    public class CreateTenantSubscriptionPaymentOrderRequestDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }
    }

    public class CreateTenantSubscriptionPaymentOrderResponseDto
    {
        public int PaymentOrderId { get; set; }

        public int InternalOrderId { get; set; }

        public string RazorpayOrderId { get; set; }

        public long AmountMinor { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public string RazorpayKeyId { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public DateTime? OrderExpiresUtc { get; set; }

        public string OrderStatus { get; set; }
    }

    public class VerifyCheckoutPaymentRequestDto
    {
        public string RazorpayOrderId { get; set; }

        public string RazorpayPaymentId { get; set; }

        public string RazorpaySignature { get; set; }
    }

    public class VerifyPaymentResponseDto
    {
        public bool IsVerified { get; set; }

        public string OrderStatus { get; set; }

        public string PaymentStatus { get; set; }

        public string SubscriptionStatus { get; set; }

        public DateTime? SubscriptionStartUtc { get; set; }

        public DateTime? SubscriptionEndUtc { get; set; }

        public DateTime? GraceEndUtc { get; set; }
    }

    public class ReconcileCapturedPaymentRequestDto
    {
        public int InternalOrderId { get; set; }

        public string RazorpayOrderId { get; set; }

        public string RazorpayPaymentId { get; set; }
    }

    public class RazorpayWebhookPayloadDto
    {
        public string EventId { get; set; }

        public string EventType { get; set; }

        public string RazorpayOrderId { get; set; }

        public string RazorpayPaymentId { get; set; }

        public RazorpayWebhookPaymentDto Payment { get; set; } = new RazorpayWebhookPaymentDto();
    }

    public class RazorpayWebhookPaymentDto
    {
        public decimal? Amount { get; set; }

        public string Currency { get; set; }

        public string Status { get; set; }

        public string Method { get; set; }

        public DateTime? PaidAtUtc { get; set; }
    }

    public class TenantSubscriptionPaymentHistoryItemDto
    {
        public int InternalOrderId { get; set; }

        public string RazorpayOrderId { get; set; }

        public string RazorpayPaymentId { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public string OrderStatus { get; set; }

        public string PaymentStatus { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public DateTime OrderCreatedUtc { get; set; }

        public DateTime? SubscriptionStartUtc { get; set; }

        public DateTime? SubscriptionEndUtc { get; set; }
    }

    public class TenantSubscriptionPaymentHistoryResponseDto
    {
        public string SubscriptionStatus { get; set; }

        public DateTime? SubscriptionStartUtc { get; set; }

        public DateTime? SubscriptionEndUtc { get; set; }

        public DateTime? GraceEndUtc { get; set; }

        public TenantSubscriptionPaymentHistoryItemDto[] Payments { get; set; } = Array.Empty<TenantSubscriptionPaymentHistoryItemDto>();
    }

    public class TenantSubscriptionOverviewResponseDto
    {
        public string TenantId { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public string SubscriptionStatus { get; set; }

        public DateTime? TrialStartUtc { get; set; }

        public DateTime? TrialEndUtc { get; set; }

        public DateTime? SubscriptionStartUtc { get; set; }

        public DateTime? SubscriptionEndUtc { get; set; }

        public DateTime? GraceEndUtc { get; set; }

        public DateTime? NextRenewalUtc { get; set; }

        public List<TenantSubscriptionOverviewPlanDto> AvailablePlans { get; set; } = new List<TenantSubscriptionOverviewPlanDto>();

        public List<TenantSubscriptionOverviewPaymentHistoryItemDto> PaymentHistory { get; set; } = new List<TenantSubscriptionOverviewPaymentHistoryItemDto>();
    }

    public class TenantSubscriptionOverviewPlanDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public bool IsActive { get; set; }
    }

    public class TenantSubscriptionOverviewPaymentHistoryItemDto
    {
        public int PaymentOrderId { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public string OrderStatus { get; set; }

        public string GatewayPaymentId { get; set; }

        public string FailureReason { get; set; }

        public DateTime? CreatedUtc { get; set; }

        public DateTime? ModifiedUtc { get; set; }
    }
}
