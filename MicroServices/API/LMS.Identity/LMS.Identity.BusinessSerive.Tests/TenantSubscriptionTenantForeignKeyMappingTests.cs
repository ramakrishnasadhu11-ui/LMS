using System;
using System.Linq;
using LMS.Identity.BusinessSerive.Data;
using LMS.Identity.DTO.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LMS.Identity.BusinessSerive.Tests
{
    public class TenantSubscriptionTenantForeignKeyMappingTests
    {
        [Fact]
        public void Phase1A_Entities_Use_Required_TenantEntityId_ForeignKey_With_Restrict_Delete()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;

            using var context = new IdentityDbContext(options);
            var model = context.Model;

            AssertTenantForeignKey(model, typeof(TenantSubscriptionPaymentOrderEntity));
            AssertTenantForeignKey(model, typeof(TenantSubscriptionPaymentTransactionEntity));
            AssertTenantForeignKey(model, typeof(TenantSubscriptionWebhookEventEntity));
            AssertTenantForeignKey(model, typeof(TenantSubscriptionStateHistoryEntity));
        }

        private static void AssertTenantForeignKey(Microsoft.EntityFrameworkCore.Metadata.IModel model, Type entityType)
        {
            var entity = model.FindEntityType(entityType);
            Assert.NotNull(entity);

            var tenantFk = entity.GetForeignKeys()
                .Single(fk => fk.PrincipalEntityType.ClrType == typeof(TenantEntity)
                    && fk.Properties.Count == 1
                    && fk.Properties[0].Name == "TenantEntityId");

            Assert.True(tenantFk.IsRequired);
            Assert.Equal(DeleteBehavior.Restrict, tenantFk.DeleteBehavior);
            Assert.Equal("TenantEntityId", tenantFk.Properties[0].Name);
            Assert.Equal("Id", tenantFk.PrincipalKey.Properties.Single().Name);
        }
    }
}
