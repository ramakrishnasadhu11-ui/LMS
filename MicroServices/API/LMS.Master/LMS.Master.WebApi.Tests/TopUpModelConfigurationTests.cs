using LMS.Master.BusinessSerive.Data;
using LMS.Master.DTO.Entity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class TopUpModelConfigurationTests
    {
        [Fact]
        public void TopUpServiceMaster_UsesExpectedDecimalType_AndIndexes()
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=TopUpModelConfigurationTests;Trusted_Connection=True;")
                .Options;

            using var context = new MasterDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(TopUpServiceMasterEntity));

            Assert.NotNull(entityType);
            Assert.Equal("TopUpServiceMaster", entityType.GetTableName());
            Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(TopUpServiceMasterEntity.UnitPrice)).GetColumnType());

            Assert.Contains(entityType.GetIndexes(), index =>
                index.IsUnique
                && index.Properties.Count == 3
                && index.Properties[0].Name == nameof(TopUpServiceMasterEntity.TenantName)
                && index.Properties[1].Name == nameof(TopUpServiceMasterEntity.StoreCode)
                && index.Properties[2].Name == nameof(TopUpServiceMasterEntity.TopUpName));

            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 4
                && index.Properties[0].Name == nameof(TopUpServiceMasterEntity.TenantName)
                && index.Properties[1].Name == nameof(TopUpServiceMasterEntity.StoreCode)
                && index.Properties[2].Name == nameof(TopUpServiceMasterEntity.IsActive)
                && index.Properties[3].Name == nameof(TopUpServiceMasterEntity.IsArchived));
        }

        [Fact]
        public void TopUpServiceApplicability_UsesExpectedForeignKeyDeleteBehavior_AndIndexes()
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=TopUpModelConfigurationTests;Trusted_Connection=True;")
                .Options;

            using var context = new MasterDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(TopUpServiceApplicabilityEntity));

            Assert.NotNull(entityType);
            Assert.Equal("TopUpServiceApplicability", entityType.GetTableName());

            var foreignKey = Assert.Single(entityType.GetForeignKeys());
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.Equal(nameof(TopUpServiceApplicabilityEntity.TopUpServiceMasterId), Assert.Single(foreignKey.Properties).Name);
            Assert.Equal(typeof(TopUpServiceMasterEntity), foreignKey.PrincipalEntityType.ClrType);

            Assert.Contains(entityType.GetIndexes(), index =>
                index.IsUnique
                && index.Properties.Count == 6
                && index.Properties[0].Name == nameof(TopUpServiceApplicabilityEntity.TenantName)
                && index.Properties[1].Name == nameof(TopUpServiceApplicabilityEntity.StoreCode)
                && index.Properties[2].Name == nameof(TopUpServiceApplicabilityEntity.TopUpServiceMasterId)
                && index.Properties[3].Name == nameof(TopUpServiceApplicabilityEntity.ServiceType)
                && index.Properties[4].Name == nameof(TopUpServiceApplicabilityEntity.Category)
                && index.Properties[5].Name == nameof(TopUpServiceApplicabilityEntity.ItemName));
        }

        [Fact]
        public void LaundryOrderTopUp_UsesExpectedDecimalTypes_ForeignKey_AndIndexes()
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=TopUpModelConfigurationTests;Trusted_Connection=True;")
                .Options;

            using var context = new MasterDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(LaundryOrderTopUpEntity));

            Assert.NotNull(entityType);
            Assert.Equal("LaundryOrderTopUps", entityType.GetTableName());
            Assert.Equal("decimal(18,3)", entityType.FindProperty(nameof(LaundryOrderTopUpEntity.AppliedQuantity)).GetColumnType());
            Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(LaundryOrderTopUpEntity.UnitPriceSnapshot)).GetColumnType());
            Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(LaundryOrderTopUpEntity.LineAmountSnapshot)).GetColumnType());

            var foreignKey = Assert.Single(entityType.GetForeignKeys());
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.Equal(nameof(LaundryOrderTopUpEntity.LaundryOrderId), Assert.Single(foreignKey.Properties).Name);
            Assert.Equal(typeof(LaundryOrderEntity), foreignKey.PrincipalEntityType.ClrType);

            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 2
                && index.Properties[0].Name == nameof(LaundryOrderTopUpEntity.LaundryOrderId)
                && index.Properties[1].Name == nameof(LaundryOrderTopUpEntity.LineNo));

            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 4
                && index.Properties[0].Name == nameof(LaundryOrderTopUpEntity.TenantName)
                && index.Properties[1].Name == nameof(LaundryOrderTopUpEntity.StoreCode)
                && index.Properties[2].Name == nameof(LaundryOrderTopUpEntity.OrderNo)
                && index.Properties[3].Name == nameof(LaundryOrderTopUpEntity.LineNo));
        }
    }
}
