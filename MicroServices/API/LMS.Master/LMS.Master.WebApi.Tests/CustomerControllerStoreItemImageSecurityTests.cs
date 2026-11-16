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
    public class CustomerControllerStoreItemImageSecurityTests
    {
        [Fact]
        public async Task UploadStoreItemImage_SingleStoreClaim_WithoutHeader_ResolvesStore()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.UploadStoreItemImage("tenant@example.com", "S1", 101, It.IsAny<IFormFile>()))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse { StatusCode = "200", Message = "ok" }));

            var controller = CreateController(
                customer.Object,
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1");

            var file = BuildFile("item.jpg", "image/jpeg");
            var result = await controller.UploadStoreItemImage(101, file);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task UploadStoreItemImage_MultiStore_WithAuthorizedHeader_ResolvesHeaderStore()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            customer.Setup(x => x.UploadStoreItemImage("tenant@example.com", "S2", 101, It.IsAny<IFormFile>()))
                .ReturnsAsync(ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse { StatusCode = "200", Message = "ok" }));

            var controller = CreateController(
                customer.Object,
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2",
                headerStore: "S2");

            var file = BuildFile("item.jpg", "image/jpeg");
            var result = await controller.UploadStoreItemImage(101, file);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
            customer.VerifyAll();
        }

        [Fact]
        public async Task UploadStoreItemImage_MultiStore_WithoutActiveStore_ReturnsBadRequest()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(
                customer.Object,
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2");

            var file = BuildFile("item.jpg", "image/jpeg");
            var result = await controller.UploadStoreItemImage(101, file);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
            Assert.Equal("Unable to determine active store scope. Specify X-StoreCode for one authorized store.", objectResult.Value);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UploadStoreItemImage_HeaderStoreNotAuthorized_ReturnsBadRequest()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(
                customer.Object,
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1,S2",
                headerStore: "S3");

            var file = BuildFile("item.jpg", "image/jpeg");
            var result = await controller.UploadStoreItemImage(101, file);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
            Assert.Equal("Unauthorized store access.", objectResult.Value);
            customer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UploadStoreItemImage_MismatchedHeaders_ReturnBadRequest()
        {
            var customer = new Mock<ICustomer>(MockBehavior.Strict);
            var controller = CreateController(
                customer.Object,
                tenantNameClaim: "tenant@example.com",
                storeCodeClaim: null,
                storeCodesClaim: "S1",
                headerTenant: "other@example.com");

            var file = BuildFile("item.jpg", "image/jpeg");
            var result = await controller.UploadStoreItemImage(101, file);

            var objectResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
            Assert.Equal("Tenant scope mismatch.", objectResult.Value);
            customer.VerifyNoOtherCalls();
        }

        private static IFormFile BuildFile(string name, string contentType)
        {
            var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            var stream = new System.IO.MemoryStream(bytes);
            return new FormFile(stream, 0, bytes.Length, "imageFile", name)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        private static CustomerController CreateController(
            ICustomer customerService,
            string tenantNameClaim,
            string storeCodeClaim,
            string storeCodesClaim,
            string headerTenant = null,
            string headerStore = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "StoreUser")
            };

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

            if (!string.IsNullOrWhiteSpace(headerTenant))
            {
                httpContext.Request.Headers["X-TenantName"] = headerTenant;
            }

            if (!string.IsNullOrWhiteSpace(headerStore))
            {
                httpContext.Request.Headers["X-StoreCode"] = headerStore;
            }

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
