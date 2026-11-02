using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.Utilities;
using LMS.Master.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerControllerWebhookSecretSecurityTests
    {
        [Fact]
        public async Task Reveal_WithoutEmailClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: null,
                storeCodesClaim: "S1");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Reveal_WithoutStoreCodesClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant@example.com",
                storeCodesClaim: null);

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Reveal_TenantEmailMismatch_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant-a@example.com",
                storeCodesClaim: "Store-1");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant-b@example.com",
                StoreCode = "Store-1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Rotate_StoreNotInStoreCodesClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant-a@example.com",
                storeCodesClaim: "Store-1,Store-3");

            var result = await controller.RotateTenantIntegrationWebhookSecret(new TenantIntegrationRotateWebhookSecretRequestDto
            {
                TenantName = "tenant-a@example.com",
                StoreCode = "Store-2",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Reveal_StoreUserRole_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "StoreUser",
                emailClaim: "tenant@example.com",
                storeCodesClaim: "S1");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Rotate_StoreUserRole_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "StoreUser",
                emailClaim: "tenant@example.com",
                storeCodesClaim: "S1");

            var result = await controller.RotateTenantIntegrationWebhookSecret(new TenantIntegrationRotateWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Reveal_Admin_WithMatchingEmailAndAssignedStore_Allows()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.RevealTenantIntegrationWebhookSecretOnce("tenant@example.com", "S1", IntegrationConstants.Category.Payments, "razorpay"))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new TenantIntegrationRevealWebhookSecretResponseDto
                {
                    ProviderKey = "razorpay",
                    WebhookSecret = "one-time-secret",
                    Message = "ok"
                }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant@example.com",
                storeCodesClaim: "S1,S2");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task Reveal_CaseInsensitiveMatching_Allows()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.RevealTenantIntegrationWebhookSecretOnce("TENANT@EXAMPLE.COM", "STORE-1", IntegrationConstants.Category.Payments, "razorpay"))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new TenantIntegrationRevealWebhookSecretResponseDto
                {
                    ProviderKey = "razorpay",
                    WebhookSecret = "one-time-secret",
                    Message = "ok"
                }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant@example.com",
                storeCodesClaim: " store-1 , STORE-2 ");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "TENANT@EXAMPLE.COM",
                StoreCode = "STORE-1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task Reveal_HeaderMismatch_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "tenant@example.com",
                storeCodesClaim: "S1",
                headerTenant: "other@example.com",
                headerStore: "S1");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Reveal_ServiceThrows_ReturnsSanitized500()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.RevealTenantIntegrationWebhookSecretOnce("t1@example.com", "S1", IntegrationConstants.Category.Payments, "razorpay"))
                .ThrowsAsync(new System.Exception("Sensitive failure detail"));

            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "t1@example.com",
                storeCodesClaim: "S1");

            var result = await controller.RevealTenantIntegrationWebhookSecretOnce(new TenantIntegrationRevealWebhookSecretRequestDto
            {
                TenantName = "t1@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Unable to process webhook secret reveal request at this time.", objectResult.Value);
            customer.VerifyAll();
        }

        [Fact]
        public async Task Rotate_ServiceThrows_ReturnsSanitized500()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.RotateTenantIntegrationWebhookSecret("t1@example.com", "S1", IntegrationConstants.Category.Payments, "razorpay"))
                .ThrowsAsync(new System.Exception("Sensitive failure detail"));

            var controller = CreateController(customer.Object,
                role: "Admin",
                emailClaim: "t1@example.com",
                storeCodesClaim: "S1");

            var result = await controller.RotateTenantIntegrationWebhookSecret(new TenantIntegrationRotateWebhookSecretRequestDto
            {
                TenantName = "t1@example.com",
                StoreCode = "S1",
                ProviderKey = "razorpay"
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Unable to process webhook secret rotation request at this time.", objectResult.Value);
            customer.VerifyAll();
        }

        private static CustomerController CreateController(ICustomer customerService, string role, string emailClaim, string storeCodesClaim, string headerTenant = null, string headerStore = null)
        {
            var claims = new List<Claim>();
            if (role != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (emailClaim != null)
            {
                claims.Add(new Claim("email", emailClaim));
            }

            if (storeCodesClaim != null)
            {
                claims.Add(new Claim("storeCodes", storeCodesClaim));
            }

            var identity = new ClaimsIdentity(claims, "testAuth");
            var principal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext
            {
                User = principal
            };

            if (!string.IsNullOrWhiteSpace(headerTenant))
            {
                httpContext.Request.Headers["X-TenantName"] = headerTenant;
            }

            if (!string.IsNullOrWhiteSpace(headerStore))
            {
                httpContext.Request.Headers["X-StoreCode"] = headerStore;
            }

            var controller = new CustomerController(customerService)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };

            return controller;
        }
    }
}
