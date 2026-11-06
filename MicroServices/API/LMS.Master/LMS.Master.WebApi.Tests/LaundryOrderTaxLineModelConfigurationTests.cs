using LMS.Master.BusinessSerive.Data;
using LMS.Master.DTO.Entity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class LaundryOrderTaxLineModelConfigurationTests
    {
        [Fact]
        public void LaundryOrderTaxLine_UsesExpectedDecimalColumnTypes()
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LaundryOrderTaxLineModelConfigurationTests;Trusted_Connection=True;")
                .Options;

            using var context = new MasterDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(LaundryOrderTaxLineEntity));

            Assert.NotNull(entityType);
            Assert.Equal("decimal(9,4)", entityType.FindProperty(nameof(LaundryOrderTaxLineEntity.RatePercent)).GetColumnType());
            Assert.Equal("decimal(18,4)", entityType.FindProperty(nameof(LaundryOrderTaxLineEntity.TaxableBaseAmount)).GetColumnType());
            Assert.Equal("decimal(18,2)", entityType.FindProperty(nameof(LaundryOrderTaxLineEntity.TaxAmount)).GetColumnType());
        }

        [Fact]
        public void LaundryOrderTaxLine_UsesNoActionDeleteBehavior_AndExpectedIndexes()
        {
            var options = new DbContextOptionsBuilder<MasterDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LaundryOrderTaxLineModelConfigurationTests;Trusted_Connection=True;")
                .Options;

            using var context = new MasterDbContext(options);
            var entityType = context.Model.FindEntityType(typeof(LaundryOrderTaxLineEntity));

            Assert.NotNull(entityType);

            var foreignKey = Assert.Single(entityType.GetForeignKeys());
            Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
            Assert.Equal("LaundryOrderId", Assert.Single(foreignKey.Properties).Name);
            Assert.Equal(typeof(LaundryOrderEntity), foreignKey.PrincipalEntityType.ClrType);

            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 2 &&
                index.Properties[0].Name == nameof(LaundryOrderTaxLineEntity.LaundryOrderId) &&
                index.Properties[1].Name == nameof(LaundryOrderTaxLineEntity.LineNo));

            Assert.Contains(entityType.GetIndexes(), index =>
                index.Properties.Count == 4 &&
                index.Properties[0].Name == nameof(LaundryOrderTaxLineEntity.TenantName) &&
                index.Properties[1].Name == nameof(LaundryOrderTaxLineEntity.StoreCode) &&
                index.Properties[2].Name == nameof(LaundryOrderTaxLineEntity.OrderNo) &&
                index.Properties[3].Name == nameof(LaundryOrderTaxLineEntity.LineNo));
        }
    }
}
