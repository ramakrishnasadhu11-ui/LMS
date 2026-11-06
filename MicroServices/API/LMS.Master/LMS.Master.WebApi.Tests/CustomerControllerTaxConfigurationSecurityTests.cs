using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.Utilities;
using LMS.Master.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerControllerTaxConfigurationSecurityTests
    {
        [Fact]
        public async Task GetTaxConfiguration_AdminWithMatchingTenantAndStoreClaims_ReturnsSuccess()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.GetTaxConfiguration("tenant@example.com", "S1"))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new { ok = true }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.GetTaxConfiguration("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task GetTaxConfiguration_WithoutTenantClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: null,
                storeCodeClaim: "S1");

            var result = await controller.GetTaxConfiguration("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetTaxConfiguration_WithoutStoreClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: null);

            var result = await controller.GetTaxConfiguration("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ResolveTaxMode_AdminWithMatchingTenantAndStoreClaims_ReturnsSuccess()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.ResolveTaxMode("tenant@example.com", "S1"))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new { resolvedTaxMode = "exclusive" }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.ResolveTaxMode("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task ResolveTaxMode_WithoutTenantClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: null,
                storeCodeClaim: "S1");

            var result = await controller.ResolveTaxMode("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ResolveTaxMode_WithoutStoreClaim_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: null);

            var result = await controller.ResolveTaxMode("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetTaxConfiguration_StoreNotInAllowedStoreCodes_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S2,S3");

            var result = await controller.GetTaxConfiguration("tenant@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetTaxConfiguration_TenantMismatch_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.GetTaxConfiguration("other@example.com", "S1");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetTaxConfiguration_MultiStoreSwitch_AllowedStore_ReturnsSuccess()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.GetTaxConfiguration("tenant@example.com", "S2"))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new { ok = true }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.GetTaxConfiguration("tenant@example.com", "S2");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        private static CustomerController CreateController(ICustomer customerService, string role, string tenantNameClaim, string storeCodeClaim, string storeCodesClaim = null)
        {
            var claims = new List<Claim>();
            if (role != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (tenantNameClaim != null)
            {
                claims.Add(new Claim("tenantName", tenantNameClaim));
            }

            if (storeCodeClaim != null)
            {
                claims.Add(new Claim("storeCode", storeCodeClaim));
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

            return new CustomerController(customerService)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
        }
    }
}
