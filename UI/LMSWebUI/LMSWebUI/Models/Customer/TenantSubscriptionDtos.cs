using System;
using System.Collections.Generic;

namespace LMSWebUI.Models.Customer
{
    public class TenantSubscriptionOverviewDto
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

        public List<TenantSubscriptionPlanOptionDto> AvailablePlans { get; set; } = new List<TenantSubscriptionPlanOptionDto>();

        public List<TenantSubscriptionPaymentHistoryItemDto> PaymentHistory { get; set; } = new List<TenantSubscriptionPaymentHistoryItemDto>();
    }

    public class TenantSubscriptionPlanOptionDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public bool IsActive { get; set; }
    }

    public class CreateTenantSubscriptionPaymentOrderRequestDto
    {
        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public string ClientRequestId { get; set; }
    }

    public class CreateTenantSubscriptionPaymentOrderResponseDto
    {
        public int PaymentOrderId { get; set; }

        public string RazorpayOrderId { get; set; }

        public string RazorpayKeyId { get; set; }

        public long AmountMinor { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public string OrderStatus { get; set; }

        public bool IsTestMode { get; set; }

        public DateTime? CreatedUtc { get; set; }
    }

    public class VerifyTenantSubscriptionCheckoutPaymentRequestDto
    {
        public int PaymentOrderId { get; set; }

        public string RazorpayOrderId { get; set; }

        public string RazorpayPaymentId { get; set; }

        public string RazorpaySignature { get; set; }
    }

    public class VerifyTenantSubscriptionCheckoutPaymentResponseDto
    {
        public bool Success { get; set; }

        public string SubscriptionStatus { get; set; }

        public string PlanCode { get; set; }

        public string BillingCycle { get; set; }

        public DateTime? SubscriptionStartUtc { get; set; }

        public DateTime? SubscriptionEndUtc { get; set; }

        public DateTime? GraceEndUtc { get; set; }

        public string Message { get; set; }
    }

    public class TenantSubscriptionPaymentHistoryItemDto
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
