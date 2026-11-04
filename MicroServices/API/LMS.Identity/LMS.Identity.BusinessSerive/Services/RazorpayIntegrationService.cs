using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities;
using LMS.Identity.Utilities;
using LMS.Core.Repository.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace LMS.Identity.BusinessSerive.Services
{
    public class RazorpayIntegrationService : IRazorpayIntegrationService
    {
        private static readonly string[] OpenOrderStatuses = { "Pending", "Created", "PendingVerification" };
        private static readonly HashSet<string> SupportedWebhookEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "payment.captured",
            "payment.failed",
            "order.paid"
        };
        private readonly IRazorpayOptions _options;
        private readonly IUnitOfWork<IdentityDbContext> _unitOfWork;
        private readonly ILogger<RazorpayIntegrationService> _logger;

        public RazorpayIntegrationService(
            IRazorpayOptions options,
            IUnitOfWork<IdentityDbContext> unitOfWork,
            ILogger<RazorpayIntegrationService> logger)
        {
            _options = options;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public bool IsLiveMode => _options.IsLiveMode;

        public string ActiveKeyId => _options.GetActiveCredentials()?.KeyId;

        public async Task<ActionReturnType> CreateTenantSubscriptionPaymentOrder(CreateTenantSubscriptionPaymentOrderRequestDto request, ClaimsPrincipal user)
        {
            var tenantClaimId = ResolveTenantIdFromClaims(user);
            if (string.IsNullOrWhiteSpace(tenantClaimId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Unauthorized, new IOResponse
                {
                    StatusCode = "401",
                    Message = "Tenant identity is missing in authentication claims."
                });
            }

            var normalizedPlanCode = NormalizePlanCode(request?.PlanCode);
            var normalizedBillingCycle = NormalizeBillingCycle(request?.BillingCycle);

            if (string.IsNullOrWhiteSpace(normalizedPlanCode) || string.IsNullOrWhiteSpace(normalizedBillingCycle))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "PlanCode and valid BillingCycle (Monthly/Yearly) are required."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantClaimId);

            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found for authenticated identity."
                });
            }

            var nowUtc = DateTime.UtcNow;
            var existingOpenOrder = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .OrderByDescending(x => x.CreatedDateUtc)
                .FirstOrDefaultAsync(x => x.TenantEntityId == tenant.Id && OpenOrderStatuses.Contains(x.OrderStatus));

            if (existingOpenOrder != null)
            {
                if (string.Equals(existingOpenOrder.OrderStatus, "Created", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsOrderExpired(existingOpenOrder, nowUtc))
                    {
                        var isMatchingSelection = IsMatchingPlanAndCycle(existingOpenOrder, normalizedPlanCode, normalizedBillingCycle);
                        if (isMatchingSelection)
                        {
                            _logger?.LogInformation(
                                "Reusing existing subscription order. TenantId={TenantId}, InternalOrderId={InternalOrderId}, Status={Status}, HasGatewayOrderId={HasGatewayOrderId}, ExpiryUtc={ExpiryUtc}",
                                tenant.TenantId,
                                existingOpenOrder.Id,
                                existingOpenOrder.OrderStatus,
                                !string.IsNullOrWhiteSpace(existingOpenOrder.GatewayOrderId),
                                existingOpenOrder.OrderExpiresUtc);

                            return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildCreateOrderResponse(existingOpenOrder));
                        }

                        _logger?.LogInformation(
                            "Superseding existing Created order due to plan/cycle change. TenantId={TenantId}, InternalOrderId={InternalOrderId}, ExistingPlan={ExistingPlan}, ExistingCycle={ExistingCycle}, RequestedPlan={RequestedPlan}, RequestedCycle={RequestedCycle}",
                            tenant.TenantId,
                            existingOpenOrder.Id,
                            existingOpenOrder.PlanCode,
                            existingOpenOrder.BillingCycle,
                            normalizedPlanCode,
                            normalizedBillingCycle);

                        existingOpenOrder.OrderStatus = "Superseded";
                        existingOpenOrder.ModifiedDateUtc = nowUtc;
                        existingOpenOrder.PendingVerificationStatus = "Superseded";
                        existingOpenOrder.PendingVerificationUntilUtc = null;

                        await _unitOfWork.SaveChangesAsync();
                    }
                    else
                    {
                        _logger?.LogInformation(
                            "Expiring stale Created order before creating replacement. TenantId={TenantId}, InternalOrderId={InternalOrderId}, ExpiryUtc={ExpiryUtc}",
                            tenant.TenantId,
                            existingOpenOrder.Id,
                            existingOpenOrder.OrderExpiresUtc);

                        existingOpenOrder.OrderStatus = "Expired";
                        existingOpenOrder.ModifiedDateUtc = nowUtc;
                        existingOpenOrder.PendingVerificationStatus = "Expired";
                        existingOpenOrder.PendingVerificationUntilUtc = null;

                        await _unitOfWork.SaveChangesAsync();
                    }
                }
                else
                {
                    _logger?.LogWarning(
                        "Open order blocks new subscription order creation. TenantId={TenantId}, InternalOrderId={InternalOrderId}, Status={Status}, PendingVerificationStatus={PendingVerificationStatus}",
                        tenant.TenantId,
                        existingOpenOrder.Id,
                        existingOpenOrder.OrderStatus,
                        existingOpenOrder.PendingVerificationStatus);

                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                    {
                        StatusCode = "409",
                        Message = "An open subscription payment order already exists for this tenant."
                    });
                }
            }

            var activePlan = await _unitOfWork.DbContext.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlanCode == normalizedPlanCode
                    && x.BillingCycle == normalizedBillingCycle
                    && x.IsActive);

            if (activePlan == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Active subscription plan was not found."
                });
            }

            if (activePlan.Amount <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Configured subscription plan amount must be greater than zero."
                });
            }

            var orderExpiresUtc = DateTime.UtcNow.AddMinutes(15);
            var idempotencyKey = $"sub_{tenant.Id}_{Guid.NewGuid():N}";
            var amountInPaise = Convert.ToInt32(decimal.Round(activePlan.Amount * 100m, 0, MidpointRounding.AwayFromZero));

            var razorpayResult = await CreateRazorpayOrderAsync(amountInPaise, idempotencyKey);
            if (!razorpayResult.Success)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                {
                    StatusCode = "502",
                    Message = razorpayResult.ErrorMessage
                });
            }

            var localOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = activePlan.PlanCode,
                BillingCycle = activePlan.BillingCycle,
                Amount = activePlan.Amount,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = razorpayResult.RazorpayOrderId,
                ReceiptId = idempotencyKey,
                CreatedDateUtc = DateTime.UtcNow,
                ModifiedDateUtc = null,
                OrderExpiresUtc = orderExpiresUtc,
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = orderExpiresUtc
            };

            _unitOfWork.DbContext.TenantSubscriptionPaymentOrders.Add(localOrder);

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsOpenOrderDuplicateViolation(ex))
            {
                _logger?.LogWarning(ex,
                    "Concurrent open-order creation detected. Resolving by returning existing active order when possible. TenantId={TenantId}",
                    tenant.TenantId);

                var concurrentOpenOrder = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                    .AsNoTracking()
                    .OrderByDescending(x => x.CreatedDateUtc)
                    .FirstOrDefaultAsync(x => x.TenantEntityId == tenant.Id && OpenOrderStatuses.Contains(x.OrderStatus));

                if (concurrentOpenOrder != null
                    && string.Equals(concurrentOpenOrder.OrderStatus, "Created", StringComparison.OrdinalIgnoreCase)
                    && !IsOrderExpired(concurrentOpenOrder, DateTime.UtcNow))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildCreateOrderResponse(concurrentOpenOrder));
                }

                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "An open subscription payment order already exists for this tenant."
                });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildCreateOrderResponse(localOrder));
        }

        public async Task<ActionReturnType> VerifyTenantSubscriptionCheckoutPayment(VerifyCheckoutPaymentRequestDto request, ClaimsPrincipal user)
        {
            var tenantClaimId = ResolveTenantIdFromClaims(user);
            if (string.IsNullOrWhiteSpace(tenantClaimId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Unauthorized, new IOResponse
                {
                    StatusCode = "401",
                    Message = "Tenant identity is missing in authentication claims."
                });
            }

            var razorpayOrderId = (request?.RazorpayOrderId ?? string.Empty).Trim();
            var razorpayPaymentId = (request?.RazorpayPaymentId ?? string.Empty).Trim();
            var razorpaySignature = (request?.RazorpaySignature ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(razorpayOrderId)
                || string.IsNullOrWhiteSpace(razorpayPaymentId)
                || string.IsNullOrWhiteSpace(razorpaySignature))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "RazorpayOrderId, RazorpayPaymentId and RazorpaySignature are required."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantClaimId);

            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found for authenticated identity."
                });
            }

            var localOrder = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .FirstOrDefaultAsync(x => x.GatewayOrderId == razorpayOrderId
                    && x.TenantEntityId == tenant.Id);

            if (localOrder == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Subscription payment order was not found for this tenant."
                });
            }

            if (IsOrderSuperseded(localOrder.OrderStatus))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "Subscription payment order is no longer active for verification."
                });
            }

            var existingTransaction = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                    && x.IsLiveMode == IsLiveMode
                    && x.GatewayPaymentId == razorpayPaymentId);

            if (existingTransaction != null && existingTransaction.TenantSubscriptionPaymentOrderId != localOrder.Id)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "The payment id is already associated with another order."
                });
            }

            if (localOrder.OrderStatus == "Paid"
                && existingTransaction != null
                && existingTransaction.IsSignatureVerified)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, CreateVerifyResponseDto(tenant, localOrder.OrderStatus, existingTransaction.TransactionStatus, true));
            }

            var credentials = _options.GetActiveCredentials();
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.KeySecret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                {
                    StatusCode = "502",
                    Message = "Razorpay credentials are not configured for the active mode."
                });
            }

            var generatedSignature = ComputeRazorpaySignature(credentials.KeySecret, razorpayOrderId, razorpayPaymentId);
            var isSignatureVerified = SecureConstantTimeEquals(generatedSignature, razorpaySignature);

            if (!isSignatureVerified)
            {
                await PersistVerificationFailureAsync(tenant, localOrder, existingTransaction, razorpayPaymentId, razorpayOrderId, "SignatureMismatch", "Checkout signature verification failed.");

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, CreateVerifyResponseDto(tenant, localOrder.OrderStatus, "SignatureMismatch", false));
            }

            var paymentFetch = await FetchRazorpayPaymentForVerificationAsync(razorpayPaymentId);
            if (!paymentFetch.Success)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                {
                    StatusCode = "502",
                    Message = paymentFetch.ErrorMessage
                });
            }

            var expectedAmountInPaise = Convert.ToInt32(decimal.Round(localOrder.Amount * 100m, 0, MidpointRounding.AwayFromZero));
            if (!string.Equals(paymentFetch.OrderId, razorpayOrderId, StringComparison.Ordinal)
                || paymentFetch.Amount != expectedAmountInPaise
                || !string.Equals(paymentFetch.Currency, "INR", StringComparison.OrdinalIgnoreCase)
                || !paymentFetch.IsCaptured)
            {
                await PersistVerificationFailureAsync(
                    tenant,
                    localOrder,
                    existingTransaction,
                    razorpayPaymentId,
                    razorpayOrderId,
                    "PaymentValidationFailed",
                    "Server-side payment validation failed.");

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, CreateVerifyResponseDto(tenant, localOrder.OrderStatus, "PaymentValidationFailed", false));
            }

            return await PersistCapturedPaymentAndActivateAsync(
                tenant,
                localOrder,
                existingTransaction,
                razorpayOrderId,
                razorpayPaymentId,
                paymentFetch.PaidAtUtc,
                "CheckoutVerification");
        }

        public async Task<ActionReturnType> ReconcileCapturedPayment(ReconcileCapturedPaymentRequestDto request)
        {
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            _logger?.LogInformation(
                "SuperAdmin payment reconciliation requested. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, InternalOrderId={InternalOrderId}, HasRazorpayOrderId={HasRazorpayOrderId}, HasRazorpayPaymentId={HasRazorpayPaymentId}, LiveMode={LiveMode}",
                correlationId,
                DateTime.UtcNow,
                request?.InternalOrderId ?? 0,
                !string.IsNullOrWhiteSpace(request?.RazorpayOrderId),
                !string.IsNullOrWhiteSpace(request?.RazorpayPaymentId),
                IsLiveMode);

            var internalOrderId = request?.InternalOrderId ?? 0;
            var razorpayOrderId = (request?.RazorpayOrderId ?? string.Empty).Trim();
            var razorpayPaymentId = (request?.RazorpayPaymentId ?? string.Empty).Trim();

            if (internalOrderId <= 0 || string.IsNullOrWhiteSpace(razorpayOrderId) || string.IsNullOrWhiteSpace(razorpayPaymentId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "InternalOrderId, RazorpayOrderId and RazorpayPaymentId are required."
                });
            }

            if (!IsLiveMode)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Captured payment reconciliation is allowed only in Live mode."
                });
            }

            var localOrder = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .FirstOrDefaultAsync(x => x.Id == internalOrderId);

            if (localOrder == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Subscription payment order was not found."
                });
            }

            if (!string.Equals((localOrder.GatewayOrderId ?? string.Empty).Trim(), razorpayOrderId, StringComparison.Ordinal))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Razorpay order id does not match the internal payment order."
                });
            }

            if (IsOrderSuperseded(localOrder.OrderStatus))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "Subscription payment order is no longer active for reconciliation."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == localOrder.TenantEntityId);

            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found for payment order."
                });
            }

            var paymentFetch = await FetchRazorpayPaymentForVerificationAsync(razorpayPaymentId);
            if (!paymentFetch.Success)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                {
                    StatusCode = "502",
                    Message = paymentFetch.ErrorMessage
                });
            }

            var expectedAmountInPaise = checked((int)ConvertToAmountMinor(localOrder.Amount));
            var expectedCurrency = (localOrder.Currency ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(expectedCurrency)
                || !string.Equals(paymentFetch.OrderId, razorpayOrderId, StringComparison.Ordinal)
                || paymentFetch.Amount != expectedAmountInPaise
                || !string.Equals(paymentFetch.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase)
                || !paymentFetch.IsCaptured)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = "Captured payment validation failed for reconciliation request."
                });
            }

            var existingTransaction = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                    && x.IsLiveMode == IsLiveMode
                    && x.GatewayPaymentId == razorpayPaymentId);

            if (existingTransaction != null && existingTransaction.TenantSubscriptionPaymentOrderId != localOrder.Id)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "The payment id is already associated with another order."
                });
            }

            if (existingTransaction != null
                && (existingTransaction.TenantEntityId != localOrder.TenantEntityId
                    || !string.Equals((existingTransaction.TenantId ?? string.Empty).Trim(), (localOrder.TenantId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                {
                    StatusCode = "409",
                    Message = "The payment id is associated with another tenant context."
                });
            }

            return await PersistCapturedPaymentAndActivateAsync(
                tenant,
                localOrder,
                existingTransaction,
                razorpayOrderId,
                razorpayPaymentId,
                paymentFetch.PaidAtUtc,
                "SuperAdminReconciliation");
        }

        public async Task<ActionReturnType> ProcessRazorpayWebhook(string rawPayload, string razorpaySignature)
        {
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            var startedAtUtc = DateTime.UtcNow;
            _logger?.LogInformation(
                "Razorpay webhook processing started. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, BodyLength={BodyLength}, SignaturePresent={SignaturePresent}, LiveMode={LiveMode}",
                correlationId,
                startedAtUtc,
                rawPayload?.Length ?? 0,
                !string.IsNullOrWhiteSpace(razorpaySignature),
                IsLiveMode);

            ActionReturnType BuildRejectedResponse(HttpStatusCode statusCode, string statusCodeText, string message, string reason)
            {
                _logger?.LogWarning(
                    "Razorpay webhook early rejection. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, HttpStatus={HttpStatus}, Reason={Reason}",
                    correlationId,
                    DateTime.UtcNow,
                    (int)statusCode,
                    reason);

                return ActionSet.ActionReturnType(statusCode, new IOResponse
                {
                    StatusCode = statusCodeText,
                    Message = message
                });
            }

            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Webhook payload is required.", "EmptyPayload");
            }

            if (string.IsNullOrWhiteSpace(razorpaySignature))
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Missing Razorpay signature header.", "MissingSignatureHeader");
            }

            var credentials = _options.GetActiveCredentials();
            if (credentials == null || string.IsNullOrWhiteSpace(credentials.WebhookSecret))
            {
                _logger?.LogWarning(
                    "Razorpay webhook rejected due to missing active webhook secret. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, HttpStatus={HttpStatus}",
                    correlationId,
                    DateTime.UtcNow,
                    (int)HttpStatusCode.BadGateway);
                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                {
                    StatusCode = "502",
                    Message = "Razorpay webhook secret is not configured for the active mode."
                });
            }

            var expectedSignature = ComputeHmacSha256Hex(credentials.WebhookSecret, rawPayload);
            var isSignatureValid = SecureConstantTimeEquals(expectedSignature, razorpaySignature.Trim());
            if (!isSignatureValid)
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Invalid webhook signature.", "SignatureValidationFailed");
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(rawPayload);
            }
            catch
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Invalid webhook payload.", "InvalidJsonPayload");
            }

            var eventId = ((string)payload["id"])?.Trim();
            var eventType = ((string)payload["event"])?.Trim();

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Webhook event id and event type are required.", "MissingEventIdOrType");
            }

            var duplicateEvent = await _unitOfWork.DbContext.TenantSubscriptionWebhookEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                    && x.IsLiveMode == IsLiveMode
                    && x.EventId == eventId);

            if (duplicateEvent != null)
            {
                _logger?.LogInformation(
                    "Razorpay webhook duplicate delivery ignored. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, EventType={EventType}",
                    correlationId,
                    DateTime.UtcNow,
                    eventType);
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = "Duplicate webhook delivery ignored."
                });
            }

            var paymentEntity = payload["payload"]?["payment"]?["entity"];
            var orderEntity = payload["payload"]?["order"]?["entity"];

            var gatewayOrderId = ((string)paymentEntity?["order_id"])?.Trim()
                ?? ((string)orderEntity?["id"])?.Trim();
            var gatewayPaymentId = ((string)paymentEntity?["id"])?.Trim();

            if (string.IsNullOrWhiteSpace(gatewayOrderId))
            {
                return BuildRejectedResponse(HttpStatusCode.BadRequest, "400", "Gateway order id is missing in webhook payload.", "MissingGatewayOrderId");
            }

            var localOrder = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .FirstOrDefaultAsync(x => x.GatewayOrderId == gatewayOrderId);

            if (localOrder == null)
            {
                return BuildRejectedResponse(HttpStatusCode.NotFound, "404", "No local subscription payment order found for this webhook.", "LocalOrderNotFound");
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == localOrder.TenantEntityId);

            if (tenant == null)
            {
                return BuildRejectedResponse(HttpStatusCode.NotFound, "404", "Tenant not found for webhook order.", "TenantNotFound");
            }

            await using var dbTx = await _unitOfWork.DbContext.Database.BeginTransactionAsync();

            async Task PersistWebhookEventAsync(string stage)
            {
                _logger?.LogInformation(
                    "Razorpay webhook event persistence begin. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, Stage={Stage}",
                    correlationId,
                    DateTime.UtcNow,
                    stage);

                try
                {
                    await _unitOfWork.SaveChangesAsync();
                    await dbTx.CommitAsync();
                    _logger?.LogInformation(
                        "Razorpay webhook event persistence succeeded. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, Stage={Stage}",
                        correlationId,
                        DateTime.UtcNow,
                        stage);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex,
                        "Razorpay webhook event persistence failed. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, Stage={Stage}",
                        correlationId,
                        DateTime.UtcNow,
                        stage);
                    throw;
                }
            }

            var webhookEvent = new TenantSubscriptionWebhookEventEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                TenantSubscriptionPaymentOrderId = localOrder.Id,
                EventId = eventId,
                PaymentProvider = "Razorpay",
                IsLiveMode = IsLiveMode,
                EventType = eventType,
                PayloadJson = rawPayload,
                ProcessingStatus = "Received",
                ProcessingError = null,
                ReceivedAtUtc = DateTime.UtcNow,
                ProcessedAtUtc = null,
                IsSignatureVerified = true
            };

            _unitOfWork.DbContext.TenantSubscriptionWebhookEvents.Add(webhookEvent);
            _logger?.LogInformation(
                "Razorpay webhook event tracked for persistence. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, EventType={EventType}, HasGatewayPaymentId={HasGatewayPaymentId}",
                correlationId,
                DateTime.UtcNow,
                eventType,
                !string.IsNullOrWhiteSpace(gatewayPaymentId));

            if (!SupportedWebhookEvents.Contains(eventType))
            {
                webhookEvent.ProcessingStatus = "Ignored";
                webhookEvent.ProcessingError = "Unsupported event type.";
                webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                try
                {
                    await PersistWebhookEventAsync("UnsupportedEvent");
                }
                catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                {
                    await dbTx.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Message = "Duplicate webhook delivery ignored."
                    });
                }

                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = "Unsupported webhook event recorded and ignored."
                });
            }

            if (string.Equals(eventType, "payment.captured", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventType, "order.paid", StringComparison.OrdinalIgnoreCase))
            {
                if (IsOrderSuperseded(localOrder.OrderStatus))
                {
                    webhookEvent.ProcessingStatus = "Ignored";
                    webhookEvent.ProcessingError = "Order is no longer active for payment capture events.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("SupersededOrderIgnored");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Message = "Webhook ignored because order is no longer active for capture."
                    });
                }

                if (string.IsNullOrWhiteSpace(gatewayPaymentId))
                {
                    webhookEvent.ProcessingStatus = "Failed";
                    webhookEvent.ProcessingError = "Gateway payment id is required for successful payment events.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("MissingGatewayPaymentId");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        Message = "Gateway payment id is missing for payment success event."
                    });
                }

                var paymentFetch = await FetchRazorpayPaymentAsync(gatewayPaymentId);
                if (!paymentFetch.Success)
                {
                    webhookEvent.ProcessingStatus = "Failed";
                    webhookEvent.ProcessingError = paymentFetch.ErrorMessage;
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("GatewayPaymentFetchFailed");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new IOResponse
                    {
                        StatusCode = "502",
                        Message = paymentFetch.ErrorMessage
                    });
                }

                var expectedAmountInPaise = Convert.ToInt32(decimal.Round(localOrder.Amount * 100m, 0, MidpointRounding.AwayFromZero));
                if (!string.Equals(paymentFetch.Payment.OrderId, localOrder.GatewayOrderId, StringComparison.Ordinal)
                    || paymentFetch.Payment.Amount != expectedAmountInPaise
                    || !string.Equals(paymentFetch.Payment.Currency, "INR", StringComparison.OrdinalIgnoreCase)
                    || !IsCapturedPayment(paymentFetch.Payment))
                {
                    webhookEvent.ProcessingStatus = "Rejected";
                    webhookEvent.ProcessingError = "Webhook payment validation failed.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("WebhookPaymentValidationFailed");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        Message = "Webhook payment validation failed."
                    });
                }

                var transaction = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                    .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                        && x.IsLiveMode == IsLiveMode
                        && x.GatewayPaymentId == gatewayPaymentId);

                if (transaction != null && transaction.TenantSubscriptionPaymentOrderId != localOrder.Id)
                {
                    webhookEvent.ProcessingStatus = "Rejected";
                    webhookEvent.ProcessingError = "Gateway payment id is already associated with another order.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("CrossOrderPaymentIdConflict");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                    {
                        StatusCode = "409",
                        Message = "Gateway payment id is already associated with another order."
                    });
                }

                if (transaction != null
                    && (transaction.TenantEntityId != localOrder.TenantEntityId
                        || !string.Equals((transaction.TenantId ?? string.Empty).Trim(), (localOrder.TenantId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    webhookEvent.ProcessingStatus = "Rejected";
                    webhookEvent.ProcessingError = "Gateway payment id tenant mismatch.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("CrossTenantPaymentIdConflict");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                    {
                        StatusCode = "409",
                        Message = "Gateway payment tenant mismatch."
                    });
                }

                if (transaction == null)
                {
                    transaction = new TenantSubscriptionPaymentTransactionEntity
                    {
                        TenantSubscriptionPaymentOrderId = localOrder.Id,
                        TenantId = localOrder.TenantId,
                        TenantEntityId = localOrder.TenantEntityId,
                        GatewayPaymentId = gatewayPaymentId,
                        GatewayOrderId = localOrder.GatewayOrderId,
                        PaymentProvider = "Razorpay",
                        IsLiveMode = IsLiveMode,
                        TransactionStatus = "Captured",
                        Amount = localOrder.Amount,
                        Currency = "INR",
                        CreatedDateUtc = DateTime.UtcNow,
                        PaidAtUtc = paymentFetch.Payment.PaidAtUtc ?? DateTime.UtcNow,
                        VerifiedAtUtc = DateTime.UtcNow,
                        IsSignatureVerified = true,
                        FailureCode = null,
                        FailureReason = null
                    };

                    _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions.Add(transaction);
                }
                else
                {
                    transaction.TransactionStatus = "Captured";
                    transaction.Amount = localOrder.Amount;
                    transaction.Currency = "INR";
                    transaction.GatewayOrderId = localOrder.GatewayOrderId;
                    transaction.PaidAtUtc = paymentFetch.Payment.PaidAtUtc ?? transaction.PaidAtUtc ?? DateTime.UtcNow;
                    transaction.VerifiedAtUtc = DateTime.UtcNow;
                    transaction.IsSignatureVerified = true;
                    transaction.FailureCode = null;
                    transaction.FailureReason = null;
                }

                localOrder.OrderStatus = "Paid";
                localOrder.PendingVerificationStatus = "Verified";
                localOrder.PendingVerificationUntilUtc = null;
                localOrder.ModifiedDateUtc = DateTime.UtcNow;

                var activationResult = await ActivateOrExtendSubscriptionAsync(
                    localOrder.TenantEntityId,
                    localOrder,
                    transaction.GatewayPaymentId,
                    transaction.PaidAtUtc ?? DateTime.UtcNow,
                    "Webhook");

                if (!activationResult.Success)
                {
                    webhookEvent.ProcessingStatus = "Failed";
                    webhookEvent.ProcessingError = activationResult.ErrorMessage;
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    await _unitOfWork.SaveChangesAsync();
                    await dbTx.CommitAsync();
                    _logger?.LogInformation(
                        "Razorpay webhook event persistence succeeded. CorrelationId={CorrelationId}, TimestampUtc={TimestampUtc}, Stage={Stage}",
                        correlationId,
                        DateTime.UtcNow,
                        "ActivationFailed");

                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        Message = activationResult.ErrorMessage
                    });
                }

                webhookEvent.ProcessingStatus = "Processed";
                webhookEvent.ProcessedAtUtc = DateTime.UtcNow;

                try
                {
                    await PersistWebhookEventAsync("CaptureProcessed");
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "UX_TenantSubscriptionStateHistories_ActivationKey"))
                {
                    await dbTx.RollbackAsync();

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Message = "Webhook processed successfully."
                    });
                }

                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = "Webhook processed successfully."
                });
            }

            if (string.Equals(eventType, "payment.failed", StringComparison.OrdinalIgnoreCase))
            {
                var existingCaptured = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantSubscriptionPaymentOrderId == localOrder.Id
                        && x.PaymentProvider == "Razorpay"
                        && x.IsLiveMode == IsLiveMode
                        && x.TransactionStatus == "Captured");

                if (string.Equals(localOrder.OrderStatus, "Paid", StringComparison.OrdinalIgnoreCase)
                    || existingCaptured != null)
                {
                    webhookEvent.ProcessingStatus = "Ignored";
                    webhookEvent.ProcessingError = "Order already has successful payment; failed event ignored.";
                    webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                    try
                    {
                        await PersistWebhookEventAsync("FailedEventIgnoredForPaidOrder");
                    }
                    catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                    {
                        await dbTx.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                        {
                            StatusCode = "200",
                            Message = "Duplicate webhook delivery ignored."
                        });
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Message = "Failed webhook ignored because order is already paid."
                    });
                }

                if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
                {
                    var transaction = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                        .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                            && x.IsLiveMode == IsLiveMode
                            && x.GatewayPaymentId == gatewayPaymentId);

                    if (transaction == null)
                    {
                        transaction = new TenantSubscriptionPaymentTransactionEntity
                        {
                            TenantSubscriptionPaymentOrderId = localOrder.Id,
                            TenantId = localOrder.TenantId,
                            TenantEntityId = localOrder.TenantEntityId,
                            GatewayPaymentId = gatewayPaymentId,
                            GatewayOrderId = localOrder.GatewayOrderId,
                            PaymentProvider = "Razorpay",
                            IsLiveMode = IsLiveMode,
                            TransactionStatus = "Failed",
                            Amount = localOrder.Amount,
                            Currency = "INR",
                            CreatedDateUtc = DateTime.UtcNow,
                            VerifiedAtUtc = DateTime.UtcNow,
                            IsSignatureVerified = true,
                            FailureCode = ((string)paymentEntity?["error_code"])?.Trim(),
                            FailureReason = ((string)paymentEntity?["error_description"])?.Trim()
                        };

                        _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions.Add(transaction);
                    }
                    else if (!string.Equals(transaction.TransactionStatus, "Captured", StringComparison.OrdinalIgnoreCase))
                    {
                        transaction.TransactionStatus = "Failed";
                        transaction.VerifiedAtUtc = DateTime.UtcNow;
                        transaction.IsSignatureVerified = true;
                        transaction.FailureCode = ((string)paymentEntity?["error_code"])?.Trim();
                        transaction.FailureReason = ((string)paymentEntity?["error_description"])?.Trim();
                    }
                }

                localOrder.OrderStatus = "PendingVerification";
                localOrder.PendingVerificationStatus = "PaymentFailed";
                localOrder.PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(15);
                localOrder.ModifiedDateUtc = DateTime.UtcNow;

                webhookEvent.ProcessingStatus = "Processed";
                webhookEvent.ProcessedAtUtc = DateTime.UtcNow;

                try
                {
                    await PersistWebhookEventAsync("PaymentFailedProcessed");
                }
                catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
                {
                    await dbTx.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                    {
                        StatusCode = "200",
                        Message = "Duplicate webhook delivery ignored."
                    });
                }

                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = "Failed payment webhook processed safely."
                });
            }

            webhookEvent.ProcessingStatus = "Ignored";
            webhookEvent.ProcessingError = "Event was not applicable.";
            webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
            try
            {
                await PersistWebhookEventAsync("EventNotApplicable");
            }
            catch (DbUpdateException ex) when (IsWebhookDuplicateRaceViolation(ex))
            {
                await dbTx.RollbackAsync();
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
                {
                    StatusCode = "200",
                    Message = "Duplicate webhook delivery ignored."
                });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new IOResponse
            {
                StatusCode = "200",
                Message = "Webhook recorded."
            });
        }

        public async Task<ActionReturnType> GetTenantSubscriptionPaymentHistory(ClaimsPrincipal user)
        {
            var tenantClaimId = ResolveTenantIdFromClaims(user);
            if (string.IsNullOrWhiteSpace(tenantClaimId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Unauthorized, new IOResponse
                {
                    StatusCode = "401",
                    Message = "Tenant identity is missing in authentication claims."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantClaimId);

            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found for authenticated identity."
                });
            }

            var orderRows = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .AsNoTracking()
                .Where(x => x.TenantEntityId == tenant.Id)
                .OrderByDescending(x => x.CreatedDateUtc)
                .Select(x => new
                {
                    Order = x,
                    LatestTransaction = _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                        .Where(t => t.TenantSubscriptionPaymentOrderId == x.Id)
                        .OrderByDescending(t => t.VerifiedAtUtc ?? t.CreatedDateUtc)
                        .ThenByDescending(t => t.Id)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var response = new TenantSubscriptionPaymentHistoryResponseDto
            {
                SubscriptionStatus = tenant.SubscriptionStatus,
                SubscriptionStartUtc = tenant.SubscriptionStartUtc,
                SubscriptionEndUtc = tenant.SubscriptionEndUtc,
                GraceEndUtc = tenant.GraceEndUtc,
                Payments = orderRows.Select(x => new TenantSubscriptionPaymentHistoryItemDto
                {
                    InternalOrderId = x.Order.Id,
                    RazorpayOrderId = x.Order.GatewayOrderId,
                    RazorpayPaymentId = x.LatestTransaction?.GatewayPaymentId,
                    PlanCode = x.Order.PlanCode,
                    BillingCycle = x.Order.BillingCycle,
                    Amount = x.Order.Amount,
                    Currency = x.Order.Currency,
                    OrderStatus = x.Order.OrderStatus,
                    PaymentStatus = x.LatestTransaction?.TransactionStatus,
                    PaidAtUtc = x.LatestTransaction?.PaidAtUtc,
                    OrderCreatedUtc = x.Order.CreatedDateUtc,
                    SubscriptionStartUtc = tenant.SubscriptionStartUtc,
                    SubscriptionEndUtc = tenant.SubscriptionEndUtc
                }).ToArray()
            };

            return ActionSet.ActionReturnType(HttpStatusCode.OK, response);
        }

        public async Task<ActionReturnType> GetTenantSubscriptionOverview(ClaimsPrincipal user)
        {
            var tenantClaimId = ResolveTenantIdFromClaims(user);
            if (string.IsNullOrWhiteSpace(tenantClaimId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Unauthorized, new IOResponse
                {
                    StatusCode = "401",
                    Message = "Tenant identity is missing in authentication claims."
                });
            }

            var tenant = await _unitOfWork.DbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantClaimId);

            if (tenant == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new IOResponse
                {
                    StatusCode = "404",
                    Message = "Tenant not found for authenticated identity."
                });
            }

            var plans = await _unitOfWork.DbContext.SubscriptionPlans
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.PlanCode)
                .ThenBy(x => x.BillingCycle)
                .Select(x => new TenantSubscriptionOverviewPlanDto
                {
                    PlanCode = x.PlanCode,
                    BillingCycle = x.BillingCycle,
                    Amount = x.Amount,
                    Currency = x.Currency,
                    IsActive = x.IsActive
                })
                .ToListAsync();

            var paymentRows = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .AsNoTracking()
                .Where(x => x.TenantEntityId == tenant.Id)
                .OrderByDescending(x => x.CreatedDateUtc)
                .Take(50)
                .Select(x => new
                {
                    Order = x,
                    LatestTransaction = _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                        .Where(t => t.TenantSubscriptionPaymentOrderId == x.Id)
                        .OrderByDescending(t => t.VerifiedAtUtc ?? t.CreatedDateUtc)
                        .ThenByDescending(t => t.Id)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var billingCycle = await ResolveTenantOverviewBillingCycleAsync(tenant);

            var response = new TenantSubscriptionOverviewResponseDto
            {
                TenantId = tenant.TenantId,
                PlanCode = tenant.PlanCode,
                BillingCycle = billingCycle,
                SubscriptionStatus = tenant.SubscriptionStatus,
                TrialStartUtc = tenant.TrialStartUtc,
                TrialEndUtc = tenant.TrialEndUtc,
                SubscriptionStartUtc = tenant.SubscriptionStartUtc,
                SubscriptionEndUtc = tenant.SubscriptionEndUtc,
                GraceEndUtc = tenant.GraceEndUtc,
                NextRenewalUtc = tenant.SubscriptionEndUtc,
                AvailablePlans = plans,
                PaymentHistory = paymentRows.Select(x => new TenantSubscriptionOverviewPaymentHistoryItemDto
                {
                    PaymentOrderId = x.Order.Id,
                    PlanCode = x.Order.PlanCode,
                    BillingCycle = x.Order.BillingCycle,
                    Amount = x.Order.Amount,
                    Currency = x.Order.Currency,
                    OrderStatus = x.Order.OrderStatus,
                    GatewayPaymentId = x.LatestTransaction?.GatewayPaymentId,
                    FailureReason = x.LatestTransaction?.FailureReason,
                    CreatedUtc = x.Order.CreatedDateUtc,
                    ModifiedUtc = x.Order.ModifiedDateUtc ?? x.LatestTransaction?.VerifiedAtUtc ?? x.LatestTransaction?.CreatedDateUtc
                }).ToList()
            };

            return ActionSet.ActionReturnType(HttpStatusCode.OK, response);
        }

        private async Task<string> ResolveTenantOverviewBillingCycleAsync(TenantEntity tenant)
        {
            if (tenant == null)
            {
                return null;
            }

            var normalizedPlanCode = NormalizePlanCode(tenant.PlanCode);
            if (string.IsNullOrWhiteSpace(normalizedPlanCode))
            {
                return null;
            }

            var normalizedPlanCodeUpper = normalizedPlanCode.ToUpper();
            const string activeStatusUpper = "ACTIVE";
            const string paidStatusUpper = "PAID";

            var activationOrderIds = await _unitOfWork.DbContext.TenantSubscriptionStateHistories
                .AsNoTracking()
                .Where(x => x.TenantEntityId == tenant.Id
                    && x.TenantId == tenant.TenantId
                    && x.TenantSubscriptionPaymentOrderId.HasValue
                    && x.NewSubscriptionStatus != null
                    && x.NewSubscriptionStatus.ToUpper() == activeStatusUpper)
                .OrderByDescending(x => x.ChangedAtUtc)
                .ThenByDescending(x => x.Id)
                .Select(x => x.TenantSubscriptionPaymentOrderId.Value)
                .ToListAsync();

            if (activationOrderIds.Count > 0)
            {
                foreach (var activationOrderId in activationOrderIds)
                {
                    var linkedBillingCycle = await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                        .AsNoTracking()
                        .Where(x => x.Id == activationOrderId
                            && x.TenantEntityId == tenant.Id
                            && x.TenantId == tenant.TenantId
                            && x.OrderStatus != null
                            && x.OrderStatus.ToUpper() == paidStatusUpper
                            && x.PlanCode != null
                            && x.PlanCode.ToUpper() == normalizedPlanCodeUpper)
                        .Select(x => x.BillingCycle)
                        .FirstOrDefaultAsync();

                    if (!string.IsNullOrWhiteSpace(linkedBillingCycle))
                    {
                        return linkedBillingCycle;
                    }
                }

                return null;
            }

            return await _unitOfWork.DbContext.TenantSubscriptionPaymentOrders
                .AsNoTracking()
                .Where(x => x.TenantEntityId == tenant.Id
                    && x.TenantId == tenant.TenantId
                    && x.OrderStatus != null
                    && x.OrderStatus.ToUpper() == paidStatusUpper
                    && x.PlanCode != null
                    && x.PlanCode.ToUpper() == normalizedPlanCodeUpper)
                .OrderByDescending(x => x.ModifiedDateUtc ?? x.CreatedDateUtc)
                .ThenByDescending(x => x.Id)
                .Select(x => x.BillingCycle)
                .FirstOrDefaultAsync();
        }

        private static string ResolveTenantIdFromClaims(ClaimsPrincipal user)
        {
            return user?.FindFirst("tenantId")?.Value?.Trim()
                ?? user?.FindFirst("TenantId")?.Value?.Trim()
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        }

        private static string NormalizeBillingCycle(string billingCycle)
        {
            var normalized = (billingCycle ?? string.Empty).Trim();
            if (string.Equals(normalized, "monthly", StringComparison.OrdinalIgnoreCase))
            {
                return "Monthly";
            }

            if (string.Equals(normalized, "yearly", StringComparison.OrdinalIgnoreCase))
            {
                return "Yearly";
            }

            return string.Empty;
        }

        private static string NormalizePlanCode(string planCode)
        {
            return (planCode ?? string.Empty).Trim();
        }

        private static bool IsMatchingPlanAndCycle(
            TenantSubscriptionPaymentOrderEntity existingOpenOrder,
            string requestedPlanCode,
            string requestedBillingCycle)
        {
            return string.Equals((existingOpenOrder?.PlanCode ?? string.Empty).Trim(), requestedPlanCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals((existingOpenOrder?.BillingCycle ?? string.Empty).Trim(), requestedBillingCycle, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOrderSupersededOrExpired(string orderStatus)
        {
            return string.Equals(orderStatus, "Expired", StringComparison.OrdinalIgnoreCase)
                || string.Equals(orderStatus, "Superseded", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOrderSuperseded(string orderStatus)
        {
            return string.Equals(orderStatus, "Superseded", StringComparison.OrdinalIgnoreCase);
        }

        protected virtual async Task<PaymentVerificationSnapshot> FetchRazorpayPaymentForVerificationAsync(string razorpayPaymentId)
        {
            var paymentResult = await FetchRazorpayPaymentAsync(razorpayPaymentId);
            if (!paymentResult.Success)
            {
                return PaymentVerificationSnapshot.Failed(paymentResult.ErrorMessage);
            }

            var payment = paymentResult.Payment;
            if (payment == null)
            {
                return PaymentVerificationSnapshot.Failed("Payment gateway returned an invalid payment response.");
            }

            return PaymentVerificationSnapshot.Succeeded(
                payment.OrderId,
                payment.Amount,
                payment.Currency,
                IsCapturedPayment(payment),
                payment.PaidAtUtc);
        }

        private static bool IsOrderExpired(TenantSubscriptionPaymentOrderEntity order, DateTime nowUtc)
        {
            if (order == null)
            {
                return false;
            }

            return order.OrderExpiresUtc.HasValue && order.OrderExpiresUtc.Value <= nowUtc;
        }

        private async Task<ActionReturnType> PersistCapturedPaymentAndActivateAsync(
            TenantEntity tenant,
            TenantSubscriptionPaymentOrderEntity localOrder,
            TenantSubscriptionPaymentTransactionEntity existingTransaction,
            string razorpayOrderId,
            string razorpayPaymentId,
            DateTime? paidAtUtc,
            string activationSource)
        {
            var nowUtc = DateTime.UtcNow;

            var transaction = existingTransaction ?? new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = localOrder.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PaymentProvider = "Razorpay",
                IsLiveMode = IsLiveMode,
                CreatedDateUtc = nowUtc
            };

            transaction.GatewayPaymentId = razorpayPaymentId;
            transaction.GatewayOrderId = razorpayOrderId;
            transaction.Amount = localOrder.Amount;
            transaction.Currency = "INR";
            transaction.TransactionStatus = "Captured";
            transaction.IsSignatureVerified = true;
            transaction.VerifiedAtUtc = nowUtc;
            transaction.PaidAtUtc = paidAtUtc ?? nowUtc;
            transaction.FailureCode = null;
            transaction.FailureReason = null;

            if (existingTransaction == null)
            {
                _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions.Add(transaction);
            }

            localOrder.OrderStatus = "Paid";
            localOrder.PendingVerificationStatus = "Verified";
            localOrder.PendingVerificationUntilUtc = null;
            localOrder.ModifiedDateUtc = nowUtc;

            var activationResult = await ActivateOrExtendSubscriptionAsync(
                tenant.Id,
                localOrder,
                transaction.GatewayPaymentId,
                transaction.PaidAtUtc ?? nowUtc,
                activationSource);

            if (!activationResult.Success)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                {
                    StatusCode = "400",
                    Message = activationResult.ErrorMessage
                });
            }

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsPaymentTransactionDuplicateViolation(ex))
            {
                var persistedTransaction = await _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions
                    .FirstOrDefaultAsync(x => x.PaymentProvider == "Razorpay"
                        && x.IsLiveMode == IsLiveMode
                        && x.GatewayPaymentId == razorpayPaymentId);

                if (persistedTransaction == null)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                    {
                        StatusCode = "409",
                        Message = "Unable to reconcile duplicate payment transaction."
                    });
                }

                if (persistedTransaction.TenantSubscriptionPaymentOrderId != localOrder.Id)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new IOResponse
                    {
                        StatusCode = "409",
                        Message = "The payment id is already associated with another order."
                    });
                }

                if (persistedTransaction.Amount != localOrder.Amount
                    || !string.Equals((persistedTransaction.Currency ?? string.Empty).Trim(), "INR", StringComparison.OrdinalIgnoreCase))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        Message = "Existing payment transaction does not match order amount and currency."
                    });
                }

                persistedTransaction.TransactionStatus = "Captured";
                persistedTransaction.GatewayOrderId = razorpayOrderId;
                persistedTransaction.IsSignatureVerified = true;
                persistedTransaction.VerifiedAtUtc = nowUtc;
                persistedTransaction.PaidAtUtc = persistedTransaction.PaidAtUtc ?? paidAtUtc ?? nowUtc;
                persistedTransaction.FailureCode = null;
                persistedTransaction.FailureReason = null;

                localOrder.OrderStatus = "Paid";
                localOrder.PendingVerificationStatus = "Verified";
                localOrder.PendingVerificationUntilUtc = null;
                localOrder.ModifiedDateUtc = nowUtc;

                var recoveredActivationResult = await ActivateOrExtendSubscriptionAsync(
                    tenant.Id,
                    localOrder,
                    persistedTransaction.GatewayPaymentId,
                    persistedTransaction.PaidAtUtc ?? paidAtUtc ?? nowUtc,
                    activationSource);

                if (!recoveredActivationResult.Success)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new IOResponse
                    {
                        StatusCode = "400",
                        Message = recoveredActivationResult.ErrorMessage
                    });
                }

                try
                {
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (DbUpdateException duplicateActivationEx) when (IsUniqueConstraintViolation(duplicateActivationEx, "UX_TenantSubscriptionStateHistories_ActivationKey"))
                {
                    var refreshedTenant = await _unitOfWork.DbContext.Tenants
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == tenant.Id);

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, CreateVerifyResponseDto(refreshedTenant ?? tenant, "Paid", persistedTransaction.TransactionStatus, true));
                }

                return ActionSet.ActionReturnType(HttpStatusCode.OK, CreateVerifyResponseDto(recoveredActivationResult.Tenant ?? tenant, localOrder.OrderStatus, persistedTransaction.TransactionStatus, true));
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "UX_TenantSubscriptionStateHistories_ActivationKey"))
            {
                var refreshedTenant = await _unitOfWork.DbContext.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == tenant.Id);

                return ActionSet.ActionReturnType(HttpStatusCode.OK, CreateVerifyResponseDto(refreshedTenant ?? tenant, "Paid", transaction.TransactionStatus, true));
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, CreateVerifyResponseDto(activationResult.Tenant ?? tenant, localOrder.OrderStatus, transaction.TransactionStatus, true));
        }

        private CreateTenantSubscriptionPaymentOrderResponseDto BuildCreateOrderResponse(TenantSubscriptionPaymentOrderEntity order)
        {
            var amountMinor = ConvertToAmountMinor(order?.Amount ?? 0m);

            return new CreateTenantSubscriptionPaymentOrderResponseDto
            {
                PaymentOrderId = order.Id,
                InternalOrderId = order.Id,
                RazorpayOrderId = order.GatewayOrderId,
                AmountMinor = amountMinor,
                Amount = order.Amount,
                Currency = order.Currency,
                RazorpayKeyId = ActiveKeyId,
                PlanCode = order.PlanCode,
                BillingCycle = order.BillingCycle,
                OrderExpiresUtc = order.OrderExpiresUtc,
                OrderStatus = order.OrderStatus
            };
        }

        private static long ConvertToAmountMinor(decimal amount)
        {
            return Convert.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
        }

        private static VerifyPaymentResponseDto CreateVerifyResponseDto(TenantEntity tenant, string orderStatus, string paymentStatus, bool isVerified)
        {
            return new VerifyPaymentResponseDto
            {
                IsVerified = isVerified,
                OrderStatus = orderStatus,
                PaymentStatus = paymentStatus,
                SubscriptionStatus = tenant?.SubscriptionStatus,
                SubscriptionStartUtc = tenant?.SubscriptionStartUtc,
                SubscriptionEndUtc = tenant?.SubscriptionEndUtc,
                GraceEndUtc = tenant?.GraceEndUtc
            };
        }

        private async Task<SubscriptionActivationResult> ActivateOrExtendSubscriptionAsync(
            int tenantEntityId,
            TenantSubscriptionPaymentOrderEntity paidOrder,
            string gatewayPaymentId,
            DateTime paidAtUtc,
            string activationSource)
        {
            if (paidOrder == null || paidOrder.Id <= 0)
            {
                return SubscriptionActivationResult.Failed("Subscription order is invalid for activation.");
            }

            var billingMonths = ResolveBillingCycleMonths(paidOrder.BillingCycle);
            if (billingMonths <= 0)
            {
                return SubscriptionActivationResult.Failed("Subscription billing cycle is invalid for activation.");
            }

            var paymentMarker = string.IsNullOrWhiteSpace(gatewayPaymentId)
                ? $"order:{paidOrder.GatewayOrderId}"
                : $"payment:{gatewayPaymentId.Trim()}";

            var activationIdempotencyKey = $"rzp:{(IsLiveMode ? "live" : "test")}:tenant:{tenantEntityId}:order:{paidOrder.Id}:{paymentMarker}";

            var alreadyActivated = await _unitOfWork.DbContext.TenantSubscriptionStateHistories
                .AsNoTracking()
                .AnyAsync(x => x.ActivationIdempotencyKey == activationIdempotencyKey);

            var tenant = await _unitOfWork.DbContext.Tenants.FirstOrDefaultAsync(x => x.Id == tenantEntityId);
            if (tenant == null)
            {
                return SubscriptionActivationResult.Failed("Tenant not found for subscription activation.");
            }

            if (alreadyActivated)
            {
                return SubscriptionActivationResult.Unchanged(tenant);
            }

            var nowUtc = DateTime.UtcNow;
            var paidUtc = paidAtUtc == default ? nowUtc : paidAtUtc.ToUniversalTime();

            var hasActiveSubscriptionWindow = string.Equals(tenant.SubscriptionStatus, "Active", StringComparison.OrdinalIgnoreCase)
                && tenant.SubscriptionEndUtc.HasValue
                && tenant.SubscriptionEndUtc.Value > nowUtc;

            var previousStatus = tenant.SubscriptionStatus;
            var previousPlan = tenant.PlanCode;
            var previousSubscriptionStartUtc = tenant.SubscriptionStartUtc;
            var previousSubscriptionEndUtc = tenant.SubscriptionEndUtc;

            if (hasActiveSubscriptionWindow)
            {
                tenant.SubscriptionEndUtc = tenant.SubscriptionEndUtc.Value.AddMonths(billingMonths);
                if (!tenant.SubscriptionStartUtc.HasValue)
                {
                    tenant.SubscriptionStartUtc = paidUtc;
                }
            }
            else
            {
                tenant.SubscriptionStartUtc = paidUtc;
                tenant.SubscriptionEndUtc = paidUtc.AddMonths(billingMonths);
            }

            tenant.GraceEndUtc = tenant.SubscriptionEndUtc?.AddDays(7);
            tenant.SubscriptionStatus = "Active";
            tenant.PlanCode = paidOrder.PlanCode;

            var hasGenuineTransition = !string.Equals(previousStatus, tenant.SubscriptionStatus, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previousPlan, tenant.PlanCode, StringComparison.OrdinalIgnoreCase)
                || previousSubscriptionStartUtc != tenant.SubscriptionStartUtc
                || previousSubscriptionEndUtc != tenant.SubscriptionEndUtc;

            if (hasGenuineTransition)
            {
                _unitOfWork.DbContext.TenantSubscriptionStateHistories.Add(new TenantSubscriptionStateHistoryEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PreviousSubscriptionStatus = previousStatus,
                    NewSubscriptionStatus = tenant.SubscriptionStatus,
                    PreviousPlanCode = previousPlan,
                    NewPlanCode = tenant.PlanCode,
                    ChangedBy = "PaymentGateway",
                    ChangeReason = $"{activationSource}|{paymentMarker}|billing:{paidOrder.BillingCycle}|amount:{paidOrder.Amount:0.00}",
                    ChangedAtUtc = nowUtc,
                    TenantSubscriptionPaymentOrderId = paidOrder.Id,
                    ActivationIdempotencyKey = activationIdempotencyKey
                });
            }

            return SubscriptionActivationResult.Updated(tenant);
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex, string constraintName)
        {
            if (ex?.InnerException is SqlException sqlEx)
            {
                return (sqlEx.Number == 2601 || sqlEx.Number == 2627)
                    && sqlEx.Message.IndexOf(constraintName ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return ex?.InnerException?.Message?.IndexOf(constraintName ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0
                   && ex.InnerException.Message.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWebhookDuplicateRaceViolation(DbUpdateException ex)
        {
            return IsUniqueConstraintViolation(ex, "UX_TenantSubscriptionWebhookEvents")
                || IsUniqueConstraintViolation(ex, "TenantSubscriptionWebhookEvents");
        }

        private static bool IsOpenOrderDuplicateViolation(DbUpdateException ex)
        {
            return IsUniqueConstraintViolation(ex, "UX_TenantSubscriptionPaymentOrders_TenantId_Open")
                || IsUniqueConstraintViolation(ex, "TenantSubscriptionPaymentOrders");
        }

        private static bool IsPaymentTransactionDuplicateViolation(DbUpdateException ex)
        {
            return IsUniqueConstraintViolation(ex, "UX_TenantSubscriptionPaymentTransactions_Provider_Mode_PaymentId")
                || IsUniqueConstraintViolation(ex, "TenantSubscriptionPaymentTransactions");
        }

        private static int ResolveBillingCycleMonths(string billingCycle)
        {
            if (string.Equals((billingCycle ?? string.Empty).Trim(), "Monthly", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals((billingCycle ?? string.Empty).Trim(), "Yearly", StringComparison.OrdinalIgnoreCase))
            {
                return 12;
            }

            return 0;
        }

        private async Task PersistVerificationFailureAsync(
            TenantEntity tenant,
            TenantSubscriptionPaymentOrderEntity localOrder,
            TenantSubscriptionPaymentTransactionEntity existingTransaction,
            string razorpayPaymentId,
            string razorpayOrderId,
            string failureCode,
            string failureReason)
        {
            var nowUtc = DateTime.UtcNow;
            var transaction = existingTransaction ?? new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = localOrder.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PaymentProvider = "Razorpay",
                IsLiveMode = IsLiveMode,
                CreatedDateUtc = nowUtc
            };

            transaction.GatewayPaymentId = razorpayPaymentId;
            transaction.GatewayOrderId = razorpayOrderId;
            transaction.Amount = localOrder.Amount;
            transaction.Currency = "INR";
            transaction.TransactionStatus = "Failed";
            transaction.IsSignatureVerified = false;
            transaction.VerifiedAtUtc = nowUtc;
            transaction.FailureCode = failureCode;
            transaction.FailureReason = failureReason;

            if (existingTransaction == null)
            {
                _unitOfWork.DbContext.TenantSubscriptionPaymentTransactions.Add(transaction);
            }

            localOrder.OrderStatus = "PendingVerification";
            localOrder.PendingVerificationStatus = failureCode;
            localOrder.PendingVerificationUntilUtc = nowUtc.AddMinutes(15);
            localOrder.ModifiedDateUtc = nowUtc;

            await _unitOfWork.SaveChangesAsync();
        }

        private static bool IsCapturedPayment(RazorpayPaymentGatewayResponse payment)
        {
            if (payment == null)
            {
                return false;
            }

            if (payment.Captured.HasValue)
            {
                return payment.Captured.Value;
            }

            return string.Equals(payment.Status, "captured", StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeRazorpaySignature(string keySecret, string razorpayOrderId, string razorpayPaymentId)
        {
            var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
            return ComputeHmacSha256Hex(keySecret, payload);
        }

        private static string ComputeHmacSha256Hex(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool SecureConstantTimeEquals(string expectedSignature, string actualSignature)
        {
            if (string.IsNullOrWhiteSpace(expectedSignature) || string.IsNullOrWhiteSpace(actualSignature))
            {
                return false;
            }

            try
            {
                var expectedBytes = Convert.FromHexString(expectedSignature.Trim());
                var actualBytes = Convert.FromHexString(actualSignature.Trim());

                if (expectedBytes.Length != actualBytes.Length)
                {
                    return false;
                }

                return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
            }
            catch
            {
                return false;
            }
        }

        private async Task<RazorpayOrderCreateResult> CreateRazorpayOrderAsync(int amountInPaise, string receipt)
        {
            var credentials = _options.GetActiveCredentials();
            if (credentials == null
                || string.IsNullOrWhiteSpace(credentials.KeyId)
                || string.IsNullOrWhiteSpace(credentials.KeySecret))
            {
                return RazorpayOrderCreateResult.Failed("Razorpay credentials are not configured for the active mode.");
            }

            _logger?.LogInformation(
                "Creating Razorpay order. Mode={Mode}, ActiveKeyIdConfigured={IsConfigured}, ActiveKeySecretConfigured={IsSecretConfigured}, ActiveKeyIdHint={KeyHint}",
                IsLiveMode ? "Live" : "Test",
                !string.IsNullOrWhiteSpace(credentials.KeyId),
                !string.IsNullOrWhiteSpace(credentials.KeySecret),
                MaskKeyId(credentials.KeyId));

            if (LooksLikePlaceholderValue(credentials.KeyId) || LooksLikePlaceholderValue(credentials.KeySecret))
            {
                _logger?.LogWarning(
                    "Razorpay order create is using placeholder credentials for mode {Mode}. Update environment/app settings with real values.",
                    IsLiveMode ? "Live" : "Test");
            }

            try
            {
                using var client = new HttpClient();
                var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.KeyId}:{credentials.KeySecret}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                var payload = new
                {
                    amount = amountInPaise,
                    currency = "INR",
                    receipt,
                    notes = new
                    {
                        source = "LMS.Identity"
                    }
                };

                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.razorpay.com/v1/orders", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var gatewayError = ExtractRazorpayError(responseText);
                    _logger?.LogWarning(
                        "Razorpay order create failed. StatusCode={StatusCode}, Reason={ReasonPhrase}, GatewayErrorCode={GatewayErrorCode}, GatewayErrorDescription={GatewayErrorDescription}",
                        (int)response.StatusCode,
                        response.ReasonPhrase,
                        gatewayError.Code,
                        gatewayError.Description);
                    return RazorpayOrderCreateResult.Failed("Failed to create payment order with payment gateway.");
                }

                var gatewayOrder = JsonConvert.DeserializeObject<RazorpayOrderCreateGatewayResponse>(responseText);
                if (gatewayOrder == null || string.IsNullOrWhiteSpace(gatewayOrder.Id))
                {
                    return RazorpayOrderCreateResult.Failed("Payment gateway returned an invalid order response.");
                }

                return RazorpayOrderCreateResult.Succeeded(gatewayOrder.Id.Trim());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Razorpay order create request failed. ExceptionType={ExceptionType}, ExceptionMessage={ExceptionMessage}",
                    ex.GetType().FullName,
                    ex.Message);
                return RazorpayOrderCreateResult.Failed("Unable to create payment order due to gateway communication error.");
            }
        }

        private async Task<RazorpayPaymentFetchResult> FetchRazorpayPaymentAsync(string razorpayPaymentId)
        {
            var credentials = _options.GetActiveCredentials();
            if (credentials == null
                || string.IsNullOrWhiteSpace(credentials.KeyId)
                || string.IsNullOrWhiteSpace(credentials.KeySecret))
            {
                return RazorpayPaymentFetchResult.Failed("Razorpay credentials are not configured for the active mode.");
            }

            try
            {
                using var client = new HttpClient();
                var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.KeyId}:{credentials.KeySecret}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                var response = await client.GetAsync($"https://api.razorpay.com/v1/payments/{WebUtility.UrlEncode(razorpayPaymentId)}");
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Razorpay payment fetch failed with status {StatusCode}.", (int)response.StatusCode);
                    return RazorpayPaymentFetchResult.Failed("Failed to validate payment with payment gateway.");
                }

                var payment = JsonConvert.DeserializeObject<RazorpayPaymentGatewayResponse>(responseText);
                if (payment == null || string.IsNullOrWhiteSpace(payment.Id))
                {
                    return RazorpayPaymentFetchResult.Failed("Payment gateway returned an invalid payment response.");
                }

                return RazorpayPaymentFetchResult.Succeeded(payment);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Razorpay payment fetch request failed.");
                return RazorpayPaymentFetchResult.Failed("Unable to validate payment due to gateway communication error.");
            }
        }

        private class RazorpayOrderCreateGatewayResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }
        }

        private class SubscriptionActivationResult
        {
            public bool Success { get; private set; }

            public bool IsChanged { get; private set; }

            public TenantEntity Tenant { get; private set; }

            public string ErrorMessage { get; private set; }

            public static SubscriptionActivationResult Updated(TenantEntity tenant)
            {
                return new SubscriptionActivationResult
                {
                    Success = true,
                    IsChanged = true,
                    Tenant = tenant
                };
            }

            public static SubscriptionActivationResult Unchanged(TenantEntity tenant)
            {
                return new SubscriptionActivationResult
                {
                    Success = true,
                    IsChanged = false,
                    Tenant = tenant
                };
            }

            public static SubscriptionActivationResult Failed(string message)
            {
                return new SubscriptionActivationResult
                {
                    Success = false,
                    IsChanged = false,
                    ErrorMessage = message
                };
            }
        }

        private class RazorpayPaymentGatewayResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("order_id")]
            public string OrderId { get; set; }

            [JsonProperty("amount")]
            public int Amount { get; set; }

            [JsonProperty("currency")]
            public string Currency { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("captured")]
            public bool? Captured { get; set; }

            [JsonProperty("created_at")]
            public long? CreatedAtEpoch { get; set; }

            public DateTime? PaidAtUtc
            {
                get
                {
                    if (!CreatedAtEpoch.HasValue)
                    {
                        return null;
                    }

                    return DateTimeOffset.FromUnixTimeSeconds(CreatedAtEpoch.Value).UtcDateTime;
                }
            }
        }

        private static RazorpayGatewayError ExtractRazorpayError(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new RazorpayGatewayError
                {
                    Code = "NoBody",
                    Description = "No response body"
                };
            }

            try
            {
                var token = JToken.Parse(responseText);
                var code = (string)token["error"]?["code"]
                    ?? (string)token["code"]
                    ?? "UnknownCode";

                var description = (string)token["error"]?["description"]
                    ?? (string)token["error"]?["reason"]
                    ?? (string)token["description"]
                    ?? "Unknown gateway error";

                return new RazorpayGatewayError
                {
                    Code = code.Length > 80 ? code.Substring(0, 80) : code,
                    Description = description.Length > 300 ? description.Substring(0, 300) : description
                };
            }
            catch
            {
                return new RazorpayGatewayError
                {
                    Code = "Unparseable",
                    Description = "Unparseable error response"
                };
            }
        }

        private static string MaskKeyId(string keyId)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                return "missing";
            }

            var value = keyId.Trim();
            if (value.Length <= 6)
            {
                return "***";
            }

            return $"{value.Substring(0, 4)}...{value.Substring(value.Length - 2)}";
        }

        private static bool LooksLikePlaceholderValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return normalized.StartsWith("RAZORPAY_", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("_KEY_")
                || normalized.Contains("_SECRET");
        }

        private class RazorpayGatewayError
        {
            public string Code { get; set; }

            public string Description { get; set; }
        }

        private class RazorpayOrderCreateResult
        {
            public bool Success { get; private set; }

            public string RazorpayOrderId { get; private set; }

            public string ErrorMessage { get; private set; }

            public static RazorpayOrderCreateResult Succeeded(string orderId)
            {
                return new RazorpayOrderCreateResult
                {
                    Success = true,
                    RazorpayOrderId = orderId
                };
            }

            public static RazorpayOrderCreateResult Failed(string message)
            {
                return new RazorpayOrderCreateResult
                {
                    Success = false,
                    ErrorMessage = message
                };
            }
        }

        private class RazorpayPaymentFetchResult
        {
            public bool Success { get; private set; }

            public RazorpayPaymentGatewayResponse Payment { get; private set; }

            public string ErrorMessage { get; private set; }

            public static RazorpayPaymentFetchResult Succeeded(RazorpayPaymentGatewayResponse payment)
            {
                return new RazorpayPaymentFetchResult
                {
                    Success = true,
                    Payment = payment
                };
            }

            public static RazorpayPaymentFetchResult Failed(string message)
            {
                return new RazorpayPaymentFetchResult
                {
                    Success = false,
                    ErrorMessage = message
                };
            }
        }

        protected class PaymentVerificationSnapshot
        {
            public bool Success { get; private set; }

            public string ErrorMessage { get; private set; }

            public string OrderId { get; private set; }

            public int Amount { get; private set; }

            public string Currency { get; private set; }

            public bool IsCaptured { get; private set; }

            public DateTime? PaidAtUtc { get; private set; }

            public static PaymentVerificationSnapshot Succeeded(
                string orderId,
                int amount,
                string currency,
                bool isCaptured,
                DateTime? paidAtUtc)
            {
                return new PaymentVerificationSnapshot
                {
                    Success = true,
                    OrderId = orderId,
                    Amount = amount,
                    Currency = currency,
                    IsCaptured = isCaptured,
                    PaidAtUtc = paidAtUtc
                };
            }

            public static PaymentVerificationSnapshot Failed(string message)
            {
                return new PaymentVerificationSnapshot
                {
                    Success = false,
                    ErrorMessage = message
                };
            }
        }
    }
}
