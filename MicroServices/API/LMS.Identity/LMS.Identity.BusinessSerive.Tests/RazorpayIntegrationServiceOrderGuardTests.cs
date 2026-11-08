using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LMS.Core.Repository.Repositories;
using LMS.Core.Repository.UnitOfWork;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Services;
using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class RazorpayIntegrationServiceOrderGuardTests
    {
        private static ClaimsPrincipal CreateTenantUser(string tenantId)
        {
            var identity = new ClaimsIdentity(new[] { new Claim("tenantId", tenantId) }, "test");
            return new ClaimsPrincipal(identity);
        }

        private static RazorpayIntegrationService CreateService(IdentityDbContext context)
        {
            var options = new RazorpayOptions
            {
                Mode = "Test",
                Test = new RazorpayCredentialOptions
                {
                    KeyId = string.Empty,
                    KeySecret = string.Empty,
                    WebhookSecret = "test-webhook"
                },
                Live = new RazorpayCredentialOptions()
            };

            var uow = new UnitOfWork<IdentityDbContext>(context);
            return new RazorpayIntegrationService(options, uow, NullLogger<RazorpayIntegrationService>.Instance);
        }

        private static string BuildPaymentFailedWebhookPayload(string eventId, string gatewayOrderId, string gatewayPaymentId)
        {
            return $"{{\"id\":\"{eventId}\",\"event\":\"payment.failed\",\"payload\":{{\"payment\":{{\"entity\":{{\"id\":\"{gatewayPaymentId}\",\"order_id\":\"{gatewayOrderId}\"}}}}}}}}";
        }

        private static string BuildUnsupportedWebhookPayload(string eventId, string gatewayOrderId)
        {
            return $"{{\"id\":\"{eventId}\",\"event\":\"subscription.unknown\",\"payload\":{{\"order\":{{\"entity\":{{\"id\":\"{gatewayOrderId}\"}}}}}}}}";
        }

        private static string ComputeWebhookSignature(string webhookSecret, string payload)
        {
            var key = Encoding.UTF8.GetBytes(webhookSecret ?? string.Empty);
            var bytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            using var hmac = new HMACSHA256(key);
            var hash = hmac.ComputeHash(bytes);
            var chars = new char[hash.Length * 2];
            var index = 0;
            foreach (var b in hash)
            {
                chars[index++] = GetHexNibble(b >> 4);
                chars[index++] = GetHexNibble(b & 0xF);
            }

            return new string(chars);
        }

        private static char GetHexNibble(int value)
        {
            return (char)(value < 10 ? value + '0' : value - 10 + 'a');
        }

        [Fact]
        public async Task CreateTenantSubscriptionPaymentOrder_Reuses_UnexpiredCreatedOrder()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reuse-1",
                Email = "reuse@test.com",
                TenantName = "Reuse Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var existingOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_existing_123",
                ReceiptId = "receipt_existing_123",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-2),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            context.TenantSubscriptionPaymentOrders.Add(existingOrder);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var request = new CreateTenantSubscriptionPaymentOrderRequestDto { PlanCode = "Premium", BillingCycle = "Monthly" };

            var result = await service.CreateTenantSubscriptionPaymentOrder(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<CreateTenantSubscriptionPaymentOrderResponseDto>(result.ResultSet);
            Assert.Equal(existingOrder.Id, response.PaymentOrderId);
            Assert.Equal(existingOrder.Id, response.InternalOrderId);
            Assert.Equal("order_existing_123", response.RazorpayOrderId);
            Assert.Equal(90000L, response.AmountMinor);
            Assert.Equal("Created", response.OrderStatus);

            Assert.Equal(1, context.TenantSubscriptionPaymentOrders.Count());
        }

        [Fact]
        public async Task CreateTenantSubscriptionPaymentOrder_Supersedes_MismatchedCreatedOrder_And_DoesNotReuse499ForPremium()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-supersede-1",
                Email = "supersede@test.com",
                TenantName = "Supersede Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);

            context.SubscriptionPlans.AddRange(
                new SubscriptionPlanEntity
                {
                    PlanCode = "Standard",
                    BillingCycle = "Monthly",
                    Amount = 499m,
                    Currency = "INR",
                    IsActive = true,
                    CreatedDateUtc = DateTime.UtcNow,
                    UpdatedBy = "test"
                },
                new SubscriptionPlanEntity
                {
                    PlanCode = "Premium",
                    BillingCycle = "Monthly",
                    Amount = 900m,
                    Currency = "INR",
                    IsActive = true,
                    CreatedDateUtc = DateTime.UtcNow,
                    UpdatedBy = "test"
                });
            await context.SaveChangesAsync();

            var existingStandardOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_std_499",
                ReceiptId = "receipt_std_499",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-2),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            context.TenantSubscriptionPaymentOrders.Add(existingStandardOrder);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var premiumRequest = new CreateTenantSubscriptionPaymentOrderRequestDto { PlanCode = "Premium", BillingCycle = "Monthly" };

            var result = await service.CreateTenantSubscriptionPaymentOrder(premiumRequest, CreateTenantUser(tenant.TenantId));

            // In tests, credentials are intentionally empty; gateway call fails.
            // This still proves the mismatched Standard order was NOT reused.
            Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);

            var oldOrder = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == existingStandardOrder.Id);
            Assert.Equal("Superseded", oldOrder.OrderStatus);
            Assert.Equal("Superseded", oldOrder.PendingVerificationStatus);
            Assert.NotNull(oldOrder.ModifiedDateUtc);
        }

        [Fact]
        public async Task CreateTenantSubscriptionPaymentOrder_Expires_StaleCreatedOrder_BeforeReplacementAttempt()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-expire-1",
                Email = "expire@test.com",
                TenantName = "Expire Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);

            context.SubscriptionPlans.Add(new SubscriptionPlanEntity
            {
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "INR",
                IsActive = true,
                CreatedDateUtc = DateTime.UtcNow,
                UpdatedBy = "test"
            });
            await context.SaveChangesAsync();

            var staleOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_stale_123",
                ReceiptId = "receipt_stale_123",
                CreatedDateUtc = DateTime.UtcNow.AddHours(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-20),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(-20)
            };
            context.TenantSubscriptionPaymentOrders.Add(staleOrder);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var request = new CreateTenantSubscriptionPaymentOrderRequestDto { PlanCode = "Premium", BillingCycle = "Monthly" };

            var result = await service.CreateTenantSubscriptionPaymentOrder(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
            var error = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("502", error.StatusCode);

            var persisted = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == staleOrder.Id);
            Assert.Equal("Expired", persisted.OrderStatus);
            Assert.Equal("Expired", persisted.PendingVerificationStatus);
            Assert.NotNull(persisted.ModifiedDateUtc);
        }

        [Fact]
        public async Task CreateTenantSubscriptionPaymentOrder_ConcurrentRequests_ReuseSameActiveOrder()
        {
            var dbName = Guid.NewGuid().ToString();
            var root = new InMemoryDatabaseRoot();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName, root)
                .Options;

            using (var seedContext = new IdentityDbContext(options))
            {
                var tenant = new TenantEntity
                {
                    TenantId = "tenant-concurrent-1",
                    Email = "concurrent@test.com",
                    TenantName = "Concurrent Tenant",
                    NoOfStores = 1,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                seedContext.Tenants.Add(tenant);
                await seedContext.SaveChangesAsync();

                seedContext.TenantSubscriptionPaymentOrders.Add(new TenantSubscriptionPaymentOrderEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PlanCode = "Premium",
                    BillingCycle = "Monthly",
                    Amount = 900m,
                    Currency = "INR",
                    OrderStatus = "Created",
                    GatewayOrderId = "order_concurrent_123",
                    ReceiptId = "receipt_concurrent_123",
                    CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                    OrderExpiresUtc = DateTime.UtcNow.AddMinutes(14),
                    PendingVerificationStatus = "NotStarted",
                    PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(14)
                });
                await seedContext.SaveChangesAsync();
            }

            using var context1 = new IdentityDbContext(options);
            using var context2 = new IdentityDbContext(options);
            using var assertContext = new IdentityDbContext(options);

            var tenantId = await assertContext.Tenants.Select(x => x.TenantId).FirstAsync();
            var request = new CreateTenantSubscriptionPaymentOrderRequestDto { PlanCode = "Premium", BillingCycle = "Monthly" };

            var service1 = CreateService(context1);
            var service2 = CreateService(context2);

            var results = await Task.WhenAll(
                service1.CreateTenantSubscriptionPaymentOrder(request, CreateTenantUser(tenantId)),
                service2.CreateTenantSubscriptionPaymentOrder(request, CreateTenantUser(tenantId)));

            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            var responses = results.Select(r => Assert.IsType<CreateTenantSubscriptionPaymentOrderResponseDto>(r.ResultSet)).ToArray();
            Assert.All(responses, r => Assert.Equal("order_concurrent_123", r.RazorpayOrderId));
            Assert.All(responses, r => Assert.True(r.PaymentOrderId > 0));
            Assert.All(responses, r => Assert.Equal(90000L, r.AmountMinor));

            Assert.Equal(1, await assertContext.TenantSubscriptionPaymentOrders.CountAsync());
        }

        [Fact]
        public async Task ProcessRazorpayWebhook_ConcurrentDuplicateDelivery_IsIdempotent()
        {
            var dbName = $"LmsIdentityWebhookRace_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=False")
                .Options;

            const string webhookSecret = "test-webhook";
            const string eventId = "evt_duplicate_unsupported_001";
            const string gatewayOrderId = "order_webhook_failed_001";

            using (var seedContext = new IdentityDbContext(options))
            {
                await seedContext.Database.EnsureDeletedAsync();
                await seedContext.Database.EnsureCreatedAsync();

                var tenant = new TenantEntity
                {
                    TenantId = "tenant-webhook-race-1",
                    Email = "webhookrace@test.com",
                    TenantName = "Webhook Race Tenant",
                    NoOfStores = 1,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                seedContext.Tenants.Add(tenant);
                await seedContext.SaveChangesAsync();

                seedContext.TenantSubscriptionPaymentOrders.Add(new TenantSubscriptionPaymentOrderEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PlanCode = "Premium",
                    BillingCycle = "Monthly",
                    Amount = 900m,
                    Currency = "INR",
                    OrderStatus = "Created",
                    GatewayOrderId = gatewayOrderId,
                    ReceiptId = "receipt_webhook_failed_001",
                    CreatedDateUtc = DateTime.UtcNow,
                    OrderExpiresUtc = DateTime.UtcNow.AddMinutes(15),
                    PendingVerificationStatus = "NotStarted",
                    PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(15)
                });
                await seedContext.SaveChangesAsync();
            }

            var payload = BuildUnsupportedWebhookPayload(eventId, gatewayOrderId);
            var signature = ComputeWebhookSignature(webhookSecret, payload);

            var razorpayOptions = new RazorpayOptions
            {
                Mode = "Test",
                Test = new RazorpayCredentialOptions
                {
                    KeyId = string.Empty,
                    KeySecret = string.Empty,
                    WebhookSecret = webhookSecret
                },
                Live = new RazorpayCredentialOptions()
            };

            using var context1 = new IdentityDbContext(options);
            using var context2 = new IdentityDbContext(options);
            using var assertContext = new IdentityDbContext(options);

            var service1 = new RazorpayIntegrationService(razorpayOptions, new DirectSaveUnitOfWork(context1), NullLogger<RazorpayIntegrationService>.Instance);
            var service2 = new RazorpayIntegrationService(razorpayOptions, new DirectSaveUnitOfWork(context2), NullLogger<RazorpayIntegrationService>.Instance);

            var firstTask = Task.Run(() => service1.ProcessRazorpayWebhook(payload, signature));
            var secondTask = Task.Run(() => service2.ProcessRazorpayWebhook(payload, signature));
            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));

            var messages = results
                .Select(x => Assert.IsType<IOResponse>(x.ResultSet).Message)
                .ToList();

            Assert.Contains("Unsupported webhook event recorded and ignored.", messages);
            Assert.Contains("Duplicate webhook delivery ignored.", messages);

            var webhookEvents = await assertContext.TenantSubscriptionWebhookEvents
                .Where(x => x.EventId == eventId)
                .ToListAsync();

            Assert.Single(webhookEvents);

            var transactions = await assertContext.TenantSubscriptionPaymentTransactions.ToListAsync();
            Assert.Empty(transactions);

            await assertContext.Database.EnsureDeletedAsync();
        }

        [Fact]
        public async Task CreateTenantSubscriptionPaymentOrder_Maps_AmountMinor_With_AwayFromZero_Rounding()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-rounding-1",
                Email = "rounding@test.com",
                TenantName = "Rounding Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var existingOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900.005m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_rounding_123",
                ReceiptId = "receipt_rounding_123",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            context.TenantSubscriptionPaymentOrders.Add(existingOrder);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var request = new CreateTenantSubscriptionPaymentOrderRequestDto { PlanCode = "Premium", BillingCycle = "Monthly" };

            var result = await service.CreateTenantSubscriptionPaymentOrder(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<CreateTenantSubscriptionPaymentOrderResponseDto>(result.ResultSet);
            Assert.Equal(existingOrder.Id, response.PaymentOrderId);
            Assert.Equal(90001L, response.AmountMinor);
            Assert.Equal(900.005m, response.Amount);
            Assert.Equal("INR", response.Currency);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_SupersededOrder_ReturnsConflict()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-verify-superseded-1",
                Email = "verifysuperseded@test.com",
                TenantName = "Verify Superseded Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var supersededOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Superseded",
                GatewayOrderId = "order_superseded_001",
                ReceiptId = "receipt_superseded_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-10),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(5),
                PendingVerificationStatus = "Superseded",
                PendingVerificationUntilUtc = null
            };

            context.TenantSubscriptionPaymentOrders.Add(supersededOrder);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var verifyRequest = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = supersededOrder.GatewayOrderId,
                RazorpayPaymentId = "pay_superseded_001",
                RazorpaySignature = "deadbeef"
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(verifyRequest, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("409", io.StatusCode);
            Assert.Equal("Subscription payment order is no longer active for verification.", io.Message);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_DuplicateTransactionRace_ReconcilesSuccessfully()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-race-recover-1",
                Email = "race-recover@test.com",
                TenantName = "Race Recover Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_race_recover_001",
                ReceiptId = "receipt_race_recover_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var existingTxn = new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = order.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                GatewayPaymentId = "pay_race_recover_001",
                GatewayOrderId = order.GatewayOrderId,
                PaymentProvider = "Razorpay",
                IsLiveMode = false,
                TransactionStatus = "Captured",
                Amount = order.Amount,
                Currency = "INR",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                IsSignatureVerified = true
            };
            context.TenantSubscriptionPaymentTransactions.Add(existingTxn);
            await context.SaveChangesAsync();

            var uow = new ThrowOnFirstSaveUnitOfWork(context, CreateDuplicateTransactionDbUpdateException());
            var service = new TestableRazorpayIntegrationService(CreateTestOptions("test-secret"), uow, order.GatewayOrderId, 49900, DateTime.UtcNow, true);

            var request = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = existingTxn.GatewayPaymentId,
                RazorpaySignature = ComputeCheckoutSignature("test-secret", order.GatewayOrderId, existingTxn.GatewayPaymentId)
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var verify = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.True(verify.IsVerified);
            Assert.Equal("Paid", verify.OrderStatus);

            var persistedOrder = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == order.Id);
            Assert.Equal("Paid", persistedOrder.OrderStatus);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_EmailFailure_DoesNotRollbackActivation()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-email-fail-1",
                Email = "email-fail@test.com",
                TenantName = "Email Failure Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_email_fail_001",
                ReceiptId = "receipt_email_fail_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret"),
                new UnitOfWork<IdentityDbContext>(context),
                order.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true,
                "INR",
                new ThrowingTenantEmailService());

            var request = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = "pay_email_fail_001",
                RazorpaySignature = ComputeCheckoutSignature("test-secret", order.GatewayOrderId, "pay_email_fail_001")
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var verify = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.True(verify.IsVerified);
            Assert.Equal("Paid", verify.OrderStatus);

            var persistedOrder = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == order.Id);
            Assert.Equal("Paid", persistedOrder.OrderStatus);

            var persistedTxn = await context.TenantSubscriptionPaymentTransactions.FirstAsync(x => x.TenantSubscriptionPaymentOrderId == order.Id);
            Assert.Equal("Captured", persistedTxn.TransactionStatus);
            Assert.Null(persistedTxn.SubscriptionActivationEmailSentAtUtc);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_DuplicateTransactionRace_WithMismatchedOrder_ReturnsConflict()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-race-mismatch-1",
                Email = "race-mismatch@test.com",
                TenantName = "Race Mismatch Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var orderA = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_race_mismatch_A",
                ReceiptId = "receipt_race_mismatch_A",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-2),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };

            var orderB = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_race_mismatch_B",
                ReceiptId = "receipt_race_mismatch_B",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };

            context.TenantSubscriptionPaymentOrders.AddRange(orderA, orderB);
            await context.SaveChangesAsync();

            var existingTxn = new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = orderA.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                GatewayPaymentId = "pay_race_mismatch_001",
                GatewayOrderId = orderA.GatewayOrderId,
                PaymentProvider = "Razorpay",
                IsLiveMode = false,
                TransactionStatus = "Captured",
                Amount = orderA.Amount,
                Currency = "INR",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                IsSignatureVerified = true
            };
            context.TenantSubscriptionPaymentTransactions.Add(existingTxn);
            await context.SaveChangesAsync();

            var uow = new ThrowOnFirstSaveUnitOfWork(context, CreateDuplicateTransactionDbUpdateException());
            var service = new TestableRazorpayIntegrationService(CreateTestOptions("test-secret"), uow, orderB.GatewayOrderId, 90000, DateTime.UtcNow, true);

            var request = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = orderB.GatewayOrderId,
                RazorpayPaymentId = existingTxn.GatewayPaymentId,
                RazorpaySignature = ComputeCheckoutSignature("test-secret", orderB.GatewayOrderId, existingTxn.GatewayPaymentId)
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("409", io.StatusCode);
            Assert.Equal("The payment id is already associated with another order.", io.Message);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_ExpiredOrder_CapturedPayment_RecoversSuccessfully()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-expired-recovery-1",
                Email = "expired-recovery@test.com",
                TenantName = "Expired Recovery Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var expiredOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_expired_recover_001",
                ReceiptId = "receipt_expired_recover_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-30),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-5),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired",
                PendingVerificationUntilUtc = null
            };

            var replacementOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_expired_recover_002",
                ReceiptId = "receipt_expired_recover_002",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-1),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                PendingVerificationStatus = "NotStarted",
                PendingVerificationUntilUtc = DateTime.UtcNow.AddMinutes(10)
            };

            context.TenantSubscriptionPaymentOrders.AddRange(expiredOrder, replacementOrder);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret"),
                new DirectSaveUnitOfWork(context),
                expiredOrder.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true);

            var request = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = expiredOrder.GatewayOrderId,
                RazorpayPaymentId = "pay_TvcGbqhoBML63S",
                RazorpaySignature = ComputeCheckoutSignature("test-secret", expiredOrder.GatewayOrderId, "pay_TvcGbqhoBML63S")
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.True(response.IsVerified);
            Assert.Equal("Paid", response.OrderStatus);

            var persistedExpired = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == expiredOrder.Id);
            var persistedReplacement = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == replacementOrder.Id);
            Assert.Equal("Paid", persistedExpired.OrderStatus);
            Assert.Equal("Created", persistedReplacement.OrderStatus);

            var txns = await context.TenantSubscriptionPaymentTransactions
                .Where(x => x.GatewayPaymentId == "pay_TvcGbqhoBML63S")
                .ToListAsync();
            Assert.Single(txns);
            Assert.Equal(expiredOrder.Id, txns[0].TenantSubscriptionPaymentOrderId);
        }

        [Fact]
        public async Task VerifyTenantSubscriptionCheckoutPayment_ExpiredOrder_MismatchedCapturedPayment_IsRejected()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-expired-reject-1",
                Email = "expired-reject@test.com",
                TenantName = "Expired Reject Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var expiredOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_expired_reject_001",
                ReceiptId = "receipt_expired_reject_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-30),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-5),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired",
                PendingVerificationUntilUtc = null
            };
            context.TenantSubscriptionPaymentOrders.Add(expiredOrder);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret"),
                new DirectSaveUnitOfWork(context),
                expiredOrder.GatewayOrderId,
                90000,
                DateTime.UtcNow,
                true);

            var request = new VerifyCheckoutPaymentRequestDto
            {
                RazorpayOrderId = expiredOrder.GatewayOrderId,
                RazorpayPaymentId = "pay_expired_reject_001",
                RazorpaySignature = ComputeCheckoutSignature("test-secret", expiredOrder.GatewayOrderId, "pay_expired_reject_001")
            };

            var result = await service.VerifyTenantSubscriptionCheckoutPayment(request, CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var response = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.False(response.IsVerified);
            Assert.Equal("PaymentValidationFailed", response.PaymentStatus);

            var persistedExpired = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == expiredOrder.Id);
            Assert.Equal("PendingVerification", persistedExpired.OrderStatus);
            Assert.Equal("PaymentValidationFailed", persistedExpired.PendingVerificationStatus);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_ExpiredOrder_CapturedPayment_RecoversSuccessfully()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-1",
                Email = "reconcile@test.com",
                TenantName = "Reconcile Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_Tvc83arl9Pud3Q",
                ReceiptId = "receipt_reconcile_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-5),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired",
                PendingVerificationUntilUtc = null
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                order.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true);

            var result = await service.ReconcileCapturedPayment(new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = order.Id,
                RazorpayOrderId = "order_Tvc83arl9Pud3Q",
                RazorpayPaymentId = "pay_TvcGbqhoBML63S"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.True(response.IsVerified);
            Assert.Equal("Paid", response.OrderStatus);

            var persistedOrder = await context.TenantSubscriptionPaymentOrders.FirstAsync(x => x.Id == order.Id);
            Assert.Equal("Paid", persistedOrder.OrderStatus);
            var persistedTransaction = await context.TenantSubscriptionPaymentTransactions.FirstAsync(x => x.GatewayPaymentId == "pay_TvcGbqhoBML63S");
            Assert.Equal(order.Id, persistedTransaction.TenantSubscriptionPaymentOrderId);
            Assert.True(persistedTransaction.IsSignatureVerified);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_Non499Order_UsesStoredAmountAndCurrency()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-900-1",
                Email = "reconcile900@test.com",
                TenantName = "Reconcile 900 Tenant",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "USD",
                OrderStatus = "Expired",
                GatewayOrderId = "order_reconcile_900_001",
                ReceiptId = "receipt_reconcile_900_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-5),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired",
                PendingVerificationUntilUtc = null
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                order.GatewayOrderId,
                90000,
                DateTime.UtcNow,
                true,
                currency: "USD");

            var result = await service.ReconcileCapturedPayment(new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = order.Id,
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = "pay_reconcile_900_001"
            });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<VerifyPaymentResponseDto>(result.ResultSet);
            Assert.True(response.IsVerified);
            Assert.Equal("Paid", response.OrderStatus);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_MismatchedPaymentValidation_ReturnsBadRequest()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-2",
                Email = "reconcile2@test.com",
                TenantName = "Reconcile Tenant 2",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_reconcile_bad_001",
                ReceiptId = "receipt_reconcile_bad_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired"
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                order.GatewayOrderId,
                90000,
                DateTime.UtcNow,
                true);

            var result = await service.ReconcileCapturedPayment(new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = order.Id,
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = "pay_reconcile_bad_001"
            });

            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("400", io.StatusCode);
            Assert.Equal("Captured payment validation failed for reconciliation request.", io.Message);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_DuplicatePaymentForAnotherOrder_ReturnsConflict()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-3",
                Email = "reconcile3@test.com",
                TenantName = "Reconcile Tenant 3",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var orderA = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_reconcile_dup_A",
                ReceiptId = "receipt_reconcile_dup_A",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired"
            };
            var orderB = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_reconcile_dup_B",
                ReceiptId = "receipt_reconcile_dup_B",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-20),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired"
            };
            context.TenantSubscriptionPaymentOrders.AddRange(orderA, orderB);
            await context.SaveChangesAsync();

            context.TenantSubscriptionPaymentTransactions.Add(new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = orderA.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                GatewayPaymentId = "pay_TvcGbqhoBML63S",
                GatewayOrderId = orderA.GatewayOrderId,
                PaymentProvider = "Razorpay",
                IsLiveMode = true,
                TransactionStatus = "Captured",
                Amount = 499m,
                Currency = "INR",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-10),
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-10),
                VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                IsSignatureVerified = true
            });
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                orderB.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true);

            var result = await service.ReconcileCapturedPayment(new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = orderB.Id,
                RazorpayOrderId = orderB.GatewayOrderId,
                RazorpayPaymentId = "pay_TvcGbqhoBML63S"
            });

            Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("409", io.StatusCode);
            Assert.Equal("The payment id is already associated with another order.", io.Message);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_SupersededOrder_ReturnsConflict()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-4",
                Email = "reconcile4@test.com",
                TenantName = "Reconcile Tenant 4",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Superseded",
                GatewayOrderId = "order_reconcile_sup_001",
                ReceiptId = "receipt_reconcile_sup_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Superseded"
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                order.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true);

            var result = await service.ReconcileCapturedPayment(new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = order.Id,
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = "pay_reconcile_sup_001"
            });

            Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
            var io = Assert.IsType<IOResponse>(result.ResultSet);
            Assert.Equal("409", io.StatusCode);
            Assert.Equal("Subscription payment order is no longer active for reconciliation.", io.Message);
        }

        [Fact]
        public async Task ReconcileCapturedPayment_RepeatedRequest_IsIdempotent()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-reconcile-5",
                Email = "reconcile5@test.com",
                TenantName = "Reconcile Tenant 5",
                NoOfStores = 1,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Expired",
                GatewayOrderId = "order_reconcile_repeat_001",
                ReceiptId = "receipt_reconcile_repeat_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-40),
                OrderExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                PendingVerificationStatus = "Expired"
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            var service = new TestableRazorpayIntegrationService(
                CreateTestOptions("test-secret", "Live"),
                new DirectSaveUnitOfWork(context),
                order.GatewayOrderId,
                49900,
                DateTime.UtcNow,
                true);

            var request = new ReconcileCapturedPaymentRequestDto
            {
                InternalOrderId = order.Id,
                RazorpayOrderId = order.GatewayOrderId,
                RazorpayPaymentId = "pay_reconcile_repeat_001"
            };

            var first = await service.ReconcileCapturedPayment(request);
            var second = await service.ReconcileCapturedPayment(request);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

            var txns = await context.TenantSubscriptionPaymentTransactions
                .Where(x => x.GatewayPaymentId == "pay_reconcile_repeat_001")
                .ToListAsync();
            Assert.Single(txns);
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_BillingCycle_PrefersLatestValidActiveActivationHistoryLinkedPaidOrder()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-1",
                Email = "overview1@test.com",
                TenantName = "Overview Tenant 1",
                NoOfStores = 1,
                PlanCode = "Premium",
                SubscriptionStatus = "Active",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            var otherTenant = new TenantEntity
            {
                TenantId = "tenant-overview-other",
                Email = "overview-other@test.com",
                TenantName = "Overview Tenant Other",
                NoOfStores = 1,
                PlanCode = "Premium",
                SubscriptionStatus = "Active",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            context.Tenants.AddRange(tenant, otherTenant);
            await context.SaveChangesAsync();

            var validOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Monthly",
                Amount = 900m,
                Currency = "INR",
                OrderStatus = "Paid",
                GatewayOrderId = "order_overview_valid_001",
                CreatedDateUtc = DateTime.UtcNow.AddDays(-10),
                ModifiedDateUtc = DateTime.UtcNow.AddDays(-9)
            };

            var unpaidOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Yearly",
                Amount = 9000m,
                Currency = "INR",
                OrderStatus = "Created",
                GatewayOrderId = "order_overview_unpaid_001",
                CreatedDateUtc = DateTime.UtcNow.AddDays(-8),
                ModifiedDateUtc = DateTime.UtcNow.AddDays(-8)
            };

            var otherTenantOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = otherTenant.TenantId,
                TenantEntityId = otherTenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Yearly",
                Amount = 9000m,
                Currency = "INR",
                OrderStatus = "Paid",
                GatewayOrderId = "order_overview_other_001",
                CreatedDateUtc = DateTime.UtcNow.AddDays(-7),
                ModifiedDateUtc = DateTime.UtcNow.AddDays(-7)
            };

            context.TenantSubscriptionPaymentOrders.AddRange(validOrder, unpaidOrder, otherTenantOrder);
            await context.SaveChangesAsync();

            context.TenantSubscriptionStateHistories.AddRange(
                new TenantSubscriptionStateHistoryEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    NewSubscriptionStatus = "Active",
                    NewPlanCode = "Premium",
                    ChangedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                    TenantSubscriptionPaymentOrderId = otherTenantOrder.Id
                },
                new TenantSubscriptionStateHistoryEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    NewSubscriptionStatus = "Active",
                    NewPlanCode = "Premium",
                    ChangedAtUtc = DateTime.UtcNow.AddMinutes(-4),
                    TenantSubscriptionPaymentOrderId = unpaidOrder.Id
                },
                new TenantSubscriptionStateHistoryEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    NewSubscriptionStatus = "Active",
                    NewPlanCode = "Premium",
                    ChangedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    TenantSubscriptionPaymentOrderId = validOrder.Id
                });

            await context.SaveChangesAsync();

            var service = CreateService(context);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            Assert.Equal("Monthly", response.BillingCycle);
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_BillingCycle_UsesLatestMatchingPaidOrderAsLegacyFallback_WhenActivationHistoryMissing()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-2",
                Email = "overview2@test.com",
                TenantName = "Overview Tenant 2",
                NoOfStores = 1,
                PlanCode = "Premium",
                SubscriptionStatus = "Active",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            context.TenantSubscriptionPaymentOrders.AddRange(
                new TenantSubscriptionPaymentOrderEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PlanCode = "Premium",
                    BillingCycle = "Monthly",
                    Amount = 900m,
                    Currency = "INR",
                    OrderStatus = "Paid",
                    GatewayOrderId = "order_overview_fallback_monthly",
                    CreatedDateUtc = DateTime.UtcNow.AddDays(-12),
                    ModifiedDateUtc = DateTime.UtcNow.AddDays(-12)
                },
                new TenantSubscriptionPaymentOrderEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PlanCode = "Premium",
                    BillingCycle = "Yearly",
                    Amount = 9000m,
                    Currency = "INR",
                    OrderStatus = "Paid",
                    GatewayOrderId = "order_overview_fallback_yearly",
                    CreatedDateUtc = DateTime.UtcNow.AddDays(-2),
                    ModifiedDateUtc = DateTime.UtcNow.AddDays(-1)
                },
                new TenantSubscriptionPaymentOrderEntity
                {
                    TenantId = tenant.TenantId,
                    TenantEntityId = tenant.Id,
                    PlanCode = "Standard",
                    BillingCycle = "Monthly",
                    Amount = 499m,
                    Currency = "INR",
                    OrderStatus = "Paid",
                    GatewayOrderId = "order_overview_fallback_other_plan",
                    CreatedDateUtc = DateTime.UtcNow,
                    ModifiedDateUtc = DateTime.UtcNow
                });

            await context.SaveChangesAsync();

            var service = CreateService(context);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            Assert.Equal("Yearly", response.BillingCycle);
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_BillingCycle_UsesActivePlanFallback_ForTrialActiveTenantWithoutOrderHistory()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-trial-1",
                Email = "overviewtrial1@test.com",
                TenantName = "Overview Trial Tenant",
                NoOfStores = 1,
                PlanCode = "Standard",
                SubscriptionStatus = "TrialActive",
                TrialStartUtc = DateTime.UtcNow.AddDays(-2),
                TrialEndUtc = DateTime.UtcNow.AddDays(43),
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);

            context.SubscriptionPlans.AddRange(
                new SubscriptionPlanEntity
                {
                    PlanCode = "Standard",
                    BillingCycle = "Monthly",
                    Amount = 499m,
                    Currency = "INR",
                    IsActive = true,
                    CreatedDateUtc = DateTime.UtcNow
                },
                new SubscriptionPlanEntity
                {
                    PlanCode = "Standard",
                    BillingCycle = "Yearly",
                    Amount = 4999m,
                    Currency = "INR",
                    IsActive = true,
                    CreatedDateUtc = DateTime.UtcNow
                });

            await context.SaveChangesAsync();

            var service = CreateService(context);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            Assert.Equal("TrialActive", response.SubscriptionStatus);
            Assert.Equal("Standard", response.PlanCode);
            Assert.Equal("Monthly", response.BillingCycle);
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_BillingCycle_FallsBackToLatestMatchingPaidOrder_WhenActivationHistoryHasNoReliableLinkedPaidOrder()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-3",
                Email = "overview3@test.com",
                TenantName = "Overview Tenant 3",
                NoOfStores = 1,
                PlanCode = "Premium",
                SubscriptionStatus = "Active",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var mismatchedPlanPaidOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Paid",
                GatewayOrderId = "order_overview_mismatch_001",
                CreatedDateUtc = DateTime.UtcNow.AddDays(-3),
                ModifiedDateUtc = DateTime.UtcNow.AddDays(-3)
            };

            var tenantPlanPaidOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Premium",
                BillingCycle = "Yearly",
                Amount = 9000m,
                Currency = "INR",
                OrderStatus = "Paid",
                GatewayOrderId = "order_overview_unlinked_paid_001",
                CreatedDateUtc = DateTime.UtcNow,
                ModifiedDateUtc = DateTime.UtcNow
            };

            context.TenantSubscriptionPaymentOrders.AddRange(mismatchedPlanPaidOrder, tenantPlanPaidOrder);
            await context.SaveChangesAsync();

            context.TenantSubscriptionStateHistories.Add(new TenantSubscriptionStateHistoryEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                NewSubscriptionStatus = "Active",
                NewPlanCode = "Premium",
                ChangedAtUtc = DateTime.UtcNow,
                TenantSubscriptionPaymentOrderId = mismatchedPlanPaidOrder.Id
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            Assert.Equal("Yearly", response.BillingCycle);
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_BillingCycle_ResolvesOnSqlServer_WithCaseInsensitiveStatusAndPlanComparisons()
        {
            var dbName = $"LmsIdentityOverviewSql_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True")
                .Options;

            await using var seedContext = new IdentityDbContext(options);
            await seedContext.Database.EnsureDeletedAsync();
            await seedContext.Database.EnsureCreatedAsync();

            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-sql-1",
                Email = "overviewsql1@test.com",
                TenantName = "Overview SQL Tenant",
                NoOfStores = 1,
                PlanCode = "premium",
                SubscriptionStatus = "Active",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            seedContext.Tenants.Add(tenant);
            await seedContext.SaveChangesAsync();

            var paidOrder = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "PREMIUM",
                BillingCycle = "Yearly",
                Amount = 9000m,
                Currency = "INR",
                OrderStatus = "paid",
                GatewayOrderId = "order_overview_sql_001",
                CreatedDateUtc = DateTime.UtcNow.AddMinutes(-10),
                ModifiedDateUtc = DateTime.UtcNow.AddMinutes(-9)
            };
            seedContext.TenantSubscriptionPaymentOrders.Add(paidOrder);
            await seedContext.SaveChangesAsync();

            seedContext.TenantSubscriptionStateHistories.Add(new TenantSubscriptionStateHistoryEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                NewSubscriptionStatus = "active",
                NewPlanCode = "PREMIUM",
                ChangedAtUtc = DateTime.UtcNow,
                TenantSubscriptionPaymentOrderId = paidOrder.Id
            });
            await seedContext.SaveChangesAsync();

            await using var testContext = new IdentityDbContext(options);
            var service = CreateService(testContext);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            Assert.Equal("Yearly", response.BillingCycle);

            await testContext.Database.EnsureDeletedAsync();
        }

        [Fact]
        public async Task GetTenantSubscriptionOverview_PaymentHistory_DoesNotDependOnUnselectedTransactionColumns()
        {
            var dbName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            using var context = new IdentityDbContext(options);
            var tenant = new TenantEntity
            {
                TenantId = "tenant-overview-projection-1",
                Email = "overviewprojection@test.com",
                TenantName = "Overview Projection Tenant",
                NoOfStores = 1,
                PlanCode = "Standard",
                SubscriptionStatus = "TrialActive",
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();

            var order = new TenantSubscriptionPaymentOrderEntity
            {
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                PlanCode = "Standard",
                BillingCycle = "Monthly",
                Amount = 499m,
                Currency = "INR",
                OrderStatus = "Paid",
                GatewayOrderId = "order_projection_001",
                CreatedDateUtc = DateTime.UtcNow.AddDays(-1),
                ModifiedDateUtc = DateTime.UtcNow
            };
            context.TenantSubscriptionPaymentOrders.Add(order);
            await context.SaveChangesAsync();

            context.TenantSubscriptionPaymentTransactions.Add(new TenantSubscriptionPaymentTransactionEntity
            {
                TenantSubscriptionPaymentOrderId = order.Id,
                TenantId = tenant.TenantId,
                TenantEntityId = tenant.Id,
                GatewayPaymentId = "pay_projection_001",
                GatewayOrderId = order.GatewayOrderId,
                PaymentProvider = "Razorpay",
                IsLiveMode = false,
                TransactionStatus = "Captured",
                Amount = order.Amount,
                Currency = "INR",
                CreatedDateUtc = DateTime.UtcNow.AddHours(-3),
                VerifiedAtUtc = DateTime.UtcNow.AddHours(-2),
                IsSignatureVerified = true,
                FailureReason = null,
                SubscriptionActivationEmailSentAtUtc = DateTime.UtcNow.AddHours(-1)
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var result = await service.GetTenantSubscriptionOverview(CreateTenantUser(tenant.TenantId));

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var response = Assert.IsType<TenantSubscriptionOverviewResponseDto>(result.ResultSet);
            var history = Assert.Single(response.PaymentHistory);
            Assert.Equal("pay_projection_001", history.GatewayPaymentId);
            Assert.Equal("Paid", history.OrderStatus);
        }

        private sealed class DirectSaveUnitOfWork : IUnitOfWork<IdentityDbContext>
        {
            public DirectSaveUnitOfWork(IdentityDbContext dbContext)
            {
                DbContext = dbContext;
            }

            public IdentityDbContext DbContext { get; }

            public void ChangeDatabase(string database)
            {
            }

            public IRepository<TEntity> GetRepository<TEntity>(bool hasCustomRepository = false) where TEntity : class
            {
                return new Repository<TEntity>(DbContext);
            }

            public int SaveChanges(bool ensureAutoHistory = false)
            {
                return DbContext.SaveChanges();
            }

            public Task<int> SaveChangesAsync(bool ensureAutoHistory = false)
            {
                return DbContext.SaveChangesAsync();
            }

            public async Task<int> SaveChangesAsync(bool ensureAutoHistory = false, params IUnitOfWork[] unitOfWorks)
            {
                var count = 0;
                if (unitOfWorks != null)
                {
                    foreach (var unitOfWork in unitOfWorks)
                    {
                        count += await unitOfWork.SaveChangesAsync(ensureAutoHistory);
                    }
                }

                count += await DbContext.SaveChangesAsync();
                return count;
            }

            public int ExecuteSqlCommand(string sql, params object[] parameters)
            {
                throw new NotImplementedException();
            }

            public IQueryable<TEntity> FromSql<TEntity>(string sql, params object[] parameters) where TEntity : class
            {
                throw new NotImplementedException();
            }

            public void TrackGraph(object rootEntity, Action<EntityEntryGraphNode> callback)
            {
                DbContext.ChangeTracker.TrackGraph(rootEntity, callback);
            }

            public void Dispose()
            {
                DbContext.Dispose();
            }
        }

        private sealed class ThrowOnFirstSaveUnitOfWork : IUnitOfWork<IdentityDbContext>
        {
            private readonly IdentityDbContext _dbContext;
            private readonly Exception _firstException;
            private int _saveCalls;

            public ThrowOnFirstSaveUnitOfWork(IdentityDbContext dbContext, Exception firstException)
            {
                _dbContext = dbContext;
                _firstException = firstException;
            }

            public IdentityDbContext DbContext => _dbContext;

            public void ChangeDatabase(string database)
            {
            }

            public IRepository<TEntity> GetRepository<TEntity>(bool hasCustomRepository = false) where TEntity : class
            {
                return new Repository<TEntity>(_dbContext);
            }

            public int SaveChanges(bool ensureAutoHistory = false)
            {
                _saveCalls++;
                if (_saveCalls == 1)
                {
                    throw _firstException;
                }

                return _dbContext.SaveChanges();
            }

            public Task<int> SaveChangesAsync(bool ensureAutoHistory = false)
            {
                _saveCalls++;
                if (_saveCalls == 1)
                {
                    throw _firstException;
                }

                return _dbContext.SaveChangesAsync();
            }

            public async Task<int> SaveChangesAsync(bool ensureAutoHistory = false, params IUnitOfWork[] unitOfWorks)
            {
                var count = 0;
                if (unitOfWorks != null)
                {
                    foreach (var unitOfWork in unitOfWorks)
                    {
                        count += await unitOfWork.SaveChangesAsync(ensureAutoHistory);
                    }
                }

                count += await SaveChangesAsync(ensureAutoHistory);
                return count;
            }

            public int ExecuteSqlCommand(string sql, params object[] parameters)
            {
                throw new NotImplementedException();
            }

            public IQueryable<TEntity> FromSql<TEntity>(string sql, params object[] parameters) where TEntity : class
            {
                throw new NotImplementedException();
            }

            public void TrackGraph(object rootEntity, Action<EntityEntryGraphNode> callback)
            {
                _dbContext.ChangeTracker.TrackGraph(rootEntity, callback);
            }

            public void Dispose()
            {
                _dbContext.Dispose();
            }
        }

        private sealed class TestableRazorpayIntegrationService : RazorpayIntegrationService
        {
            private readonly string _orderId;
            private readonly int _amount;
            private readonly DateTime? _paidAtUtc;
            private readonly bool _isCaptured;
            private readonly string _currency;
            private readonly ITenantEmailService _tenantEmailService;

            public TestableRazorpayIntegrationService(
                IRazorpayOptions options,
                IUnitOfWork<IdentityDbContext> unitOfWork,
                string orderId,
                int amount,
                DateTime? paidAtUtc,
                bool isCaptured,
                string currency = "INR",
                ITenantEmailService tenantEmailService = null)
                : base(options, unitOfWork, NullLogger<RazorpayIntegrationService>.Instance, tenantEmailService)
            {
                _orderId = orderId;
                _amount = amount;
                _paidAtUtc = paidAtUtc;
                _isCaptured = isCaptured;
                _currency = currency;
                _tenantEmailService = tenantEmailService;
            }

            protected override Task<PaymentVerificationSnapshot> FetchRazorpayPaymentForVerificationAsync(string razorpayPaymentId)
            {
                var snapshot = PaymentVerificationSnapshot.Succeeded(_orderId, _amount, _currency, _isCaptured, _paidAtUtc);
                return Task.FromResult(snapshot);
            }
        }

        private sealed class ThrowingTenantEmailService : ITenantEmailService
        {
            public Task SendMailAsync(string email, string subject, string body, string tenantId = null, string tenantEmail = null)
            {
                throw new InvalidOperationException("Simulated SMTP send failure.");
            }
        }

        private static IRazorpayOptions CreateTestOptions(string keySecret = "", string mode = "Test")
        {
            return new RazorpayOptions
            {
                Mode = mode,
                Test = new RazorpayCredentialOptions
                {
                    KeyId = "test_key",
                    KeySecret = keySecret,
                    WebhookSecret = "test-webhook"
                },
                Live = new RazorpayCredentialOptions()
            };
        }

        private static string ComputeCheckoutSignature(string keySecret, string orderId, string paymentId)
        {
            var payload = $"{orderId}|{paymentId}";
            var key = Encoding.UTF8.GetBytes(keySecret ?? string.Empty);
            var bytes = Encoding.UTF8.GetBytes(payload);
            using var hmac = new HMACSHA256(key);
            var hash = hmac.ComputeHash(bytes);
            var chars = new char[hash.Length * 2];
            var index = 0;
            foreach (var b in hash)
            {
                chars[index++] = GetHexNibble(b >> 4);
                chars[index++] = GetHexNibble(b & 0xF);
            }

            return new string(chars);
        }

        private static DbUpdateException CreateDuplicateTransactionDbUpdateException()
        {
            var inner = new Exception("Violation of UNIQUE KEY constraint 'UX_TenantSubscriptionPaymentTransactions_Provider_Mode_PaymentId'. Cannot insert duplicate key row in object 'dbo.TenantSubscriptionPaymentTransactions'.");
            return new DbUpdateException("Duplicate payment transaction", inner);
        }
    }
}
