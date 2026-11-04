using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO.Entities;
using LMS.Identity.WebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class IdentityApiAuthorizationTests : IClassFixture<IdentityApiAuthorizationTests.IdentityApiFactory>
    {
        private readonly IdentityApiFactory _factory;

        public IdentityApiAuthorizationTests(IdentityApiFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task MissingToken_Returns401_For_SuperAdminPlanEndpoints()
        {
            EnsurePlansSeeded();
            using var client = _factory.CreateClient();

            var getResponse = await client.GetAsync("/Login/GetSubscriptionPlans");
            Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

            var priceResponse = await client.PostAsync(
                "/Login/UpdateSubscriptionPlanPrice",
                JsonContent(new { planCode = "Standard", billingCycle = "Monthly", amount = 599.00m, updatedBy = "forged@actor.local" }));
            Assert.Equal(HttpStatusCode.Unauthorized, priceResponse.StatusCode);

            var activeResponse = await client.PostAsync(
                "/Login/SetSubscriptionPlanActiveStatus",
                JsonContent(new { planCode = "Standard", billingCycle = "Monthly", isActive = true, updatedBy = "forged@actor.local" }));
            Assert.Equal(HttpStatusCode.Unauthorized, activeResponse.StatusCode);

            var reconcileResponse = await client.PostAsync(
                "/Login/ReconcileCapturedPayment",
                JsonContent(new { internalOrderId = 12, razorpayOrderId = "order_Tvc83arl9Pud3Q", razorpayPaymentId = "pay_TvcGbqhoBML63S" }));
            Assert.Equal(HttpStatusCode.Unauthorized, reconcileResponse.StatusCode);
        }

        [Fact]
        public async Task NonSuperAdminToken_Returns403_For_SuperAdminPlanEndpoints()
        {
            EnsurePlansSeeded();
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _factory.CreateJwtToken("tenant-admin@lms.local", "Admin"));

            var getResponse = await client.GetAsync("/Login/GetSubscriptionPlans");
            Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

            var priceResponse = await client.PostAsync(
                "/Login/UpdateSubscriptionPlanPrice",
                JsonContent(new { planCode = "Standard", billingCycle = "Monthly", amount = 599.00m }));
            Assert.Equal(HttpStatusCode.Forbidden, priceResponse.StatusCode);

            var activeResponse = await client.PostAsync(
                "/Login/SetSubscriptionPlanActiveStatus",
                JsonContent(new { planCode = "Standard", billingCycle = "Monthly", isActive = true }));
            Assert.Equal(HttpStatusCode.Forbidden, activeResponse.StatusCode);

            var reconcileResponse = await client.PostAsync(
                "/Login/ReconcileCapturedPayment",
                JsonContent(new { internalOrderId = 12, razorpayOrderId = "order_Tvc83arl9Pud3Q", razorpayPaymentId = "pay_TvcGbqhoBML63S" }));
            Assert.Equal(HttpStatusCode.Forbidden, reconcileResponse.StatusCode);
        }

        [Fact]
        public async Task SuperAdminToken_IsAuthorized_For_SuperAdminPlanEndpoints()
        {
            EnsurePlansSeeded();
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _factory.CreateJwtToken("superadmin@lms.local", "SuperAdmin"));

            var getResponse = await client.GetAsync("/Login/GetSubscriptionPlans");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var priceResponse = await client.PostAsync(
                "/Login/UpdateSubscriptionPlanPrice",
                JsonContent(new { planCode = "Standard", billingCycle = "Monthly", amount = 611.50m }));
            Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);

            var activeResponse = await client.PostAsync(
                "/Login/SetSubscriptionPlanActiveStatus",
                JsonContent(new { planCode = "Premium", billingCycle = "Yearly", isActive = false }));
            Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        }

        [Fact]
        public async Task RegistrationLoginPasswordEndpoints_RemainAnonymous()
        {
            EnsurePlansSeeded();
            using var client = _factory.CreateClient();

            var registerResponse = await client.PostAsync("/Login/RegisterTenant", JsonContent((object)null));
            Assert.NotEqual(HttpStatusCode.Unauthorized, registerResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, registerResponse.StatusCode);

            var loginResponse = await client.PostAsync("/Login/TenantLogin", JsonContent(new { eMail = "", password = "" }));
            Assert.NotEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, loginResponse.StatusCode);

            var forgotResponse = await client.GetAsync("/Login/forgotPassword?eMail=");
            Assert.NotEqual(HttpStatusCode.Unauthorized, forgotResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, forgotResponse.StatusCode);

            var changeResponse = await client.GetAsync("/Login/changePassword?eMail=&oldPassword=&newPassword=");
            Assert.NotEqual(HttpStatusCode.Unauthorized, changeResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, changeResponse.StatusCode);
        }

        [Fact]
        public async Task ForgedUpdatedByInBody_CannotOverride_AuthenticatedIdentity()
        {
            EnsurePlansSeeded();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            using var client = _factory.CreateClient();
            var jwtActor = "superadmin@lms.local";
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _factory.CreateJwtToken(jwtActor, "SuperAdmin"));

            var response = await client.PostAsync(
                "/Login/UpdateSubscriptionPlanPrice",
                JsonContent(new
                {
                    planCode = "Standard",
                    billingCycle = "Monthly",
                    amount = 777.00m,
                    updatedBy = "forged-body-actor@attacker.local"
                }));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var updated = await db.SubscriptionPlans.FirstAsync(x => x.PlanCode == "Standard" && x.BillingCycle == "Monthly");
            Assert.Equal(jwtActor, updated.UpdatedBy);
        }

        private void EnsurePlansSeeded()
        {
            _factory.EnsurePlansSeeded();
        }

        private static StringContent JsonContent(object payload)
        {
            return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }

        public class IdentityApiFactory : WebApplicationFactory<Program>
        {
            private readonly string _dbName = $"identity-api-auth-tests-{Guid.NewGuid()}";
            private readonly string _issuer = "ExcelLaundryServices.Identity";
            private readonly string _audience = "ExcelLaundryServices.APIs";
            private readonly string _signingKey = "RKgB+QN7e6oZ6HyXjyhtEwpLv/1xp+oztlWa5DevFgs=";

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment("Testing");
                var dbName = $"LmsIdentityApiAuthTests_{Guid.NewGuid():N}";
                var conn = $"Server=(localdb)\\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["ConnectionStrings:IdentityDb"] = conn,
                        ["Jwt:Issuer"] = _issuer,
                        ["Jwt:Audience"] = _audience,
                        ["Jwt:SigningKey"] = _signingKey,
                        ["Jwt:ExpiryMinutes"] = "60",
                        ["SuperAdmin:Email"] = "superadmin@lms.local",
                        ["SuperAdmin:Password"] = "SuperAdmin@123",
                        ["SuperAdmin:DisplayName"] = "Super Admin"
                    });
                });
            }

            public string CreateJwtToken(string email, string role)
            {
                var claims = new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub, email ?? string.Empty),
                    new Claim(JwtRegisteredClaimNames.Email, email ?? string.Empty),
                    new Claim(ClaimTypes.Name, email ?? string.Empty),
                    new Claim(ClaimTypes.Role, role ?? string.Empty)
                };

                var now = DateTime.UtcNow;
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                var jwt = new JwtSecurityToken(
                    issuer: _issuer,
                    audience: _audience,
                    claims: claims,
                    notBefore: now,
                    expires: now.AddMinutes(30),
                    signingCredentials: creds);

                return new JwtSecurityTokenHandler().WriteToken(jwt);
            }

            public void EnsurePlansSeeded()
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                SeedPlans(db);
            }

            private static void SeedPlans(IdentityDbContext db)
            {
                if (!db.SubscriptionPlans.Any())
                {
                    db.SubscriptionPlans.AddRange(
                        new SubscriptionPlanEntity
                        {
                            PlanCode = "Standard",
                            BillingCycle = "Monthly",
                            Amount = 499.00m,
                            Currency = "INR",
                            IsActive = true,
                            CreatedDateUtc = DateTime.UtcNow
                        },
                        new SubscriptionPlanEntity
                        {
                            PlanCode = "Premium",
                            BillingCycle = "Yearly",
                            Amount = 8999.00m,
                            Currency = "INR",
                            IsActive = true,
                            CreatedDateUtc = DateTime.UtcNow
                        });
                    db.SaveChanges();
                }
            }
        }
    }
}
