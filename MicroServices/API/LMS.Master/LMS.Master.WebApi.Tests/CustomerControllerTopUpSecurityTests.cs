using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
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
    public class CustomerControllerTopUpSecurityTests
    {
        [Fact]
        public async Task SaveTopUpServiceMaster_WithValidClaims_UsesScopedTenantStore()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.SaveTopUpServiceMaster(It.Is<SaveTopUpServiceMasterRequestDto>(r =>
                    r.TenantName == "tenant@example.com"
                    && r.StoreCode == "S1"
                    && r.TopUpService != null
                    && r.TopUpService.TenantName == "tenant@example.com"
                    && r.TopUpService.StoreCode == "S1")))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new SaveTopUpServiceMasterResponseDto { StatusCode = "200" }));

            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = "tenant@example.com",
                StoreCode = "S1",
                TopUpService = new TopUpServiceMasterDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TopUpName = "Stain Removal",
                    ChargeType = "PerOrder",
                    UnitPrice = 9m
                }
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task SaveTopUpServiceMaster_TenantMismatch_ReturnsForbidden()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(customer.Object,
                role: "Admin",
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var result = await controller.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = "other@example.com",
                StoreCode = "S1",
                TopUpService = new TopUpServiceMasterDto
                {
                    TenantName = "other@example.com",
                    StoreCode = "S1",
                    TopUpName = "Stain Removal",
                    ChargeType = "PerOrder",
                    UnitPrice = 9m
                }
            });

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            customer.VerifyNoOtherCalls();
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
