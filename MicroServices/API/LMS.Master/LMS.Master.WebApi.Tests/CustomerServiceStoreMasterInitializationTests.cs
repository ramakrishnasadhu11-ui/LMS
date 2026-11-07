using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class CustomerServiceStoreMasterInitializationTests : IClassFixture<RazorpaySqlServerFixture>
    {
        private readonly RazorpaySqlServerFixture _fixture;

        public CustomerServiceStoreMasterInitializationTests(RazorpaySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetStoreServiceMasters_SeedsServiceAndCategory_AndStoreItems_FromLaundryDefaults()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using (var context = _fixture.CreateDbContext())
            {
                var service = _fixture.CreateService(context);

                var masterResult = await service.GetStoreServiceMasters(tenant, store, includeInactive: true);
                Assert.Equal(HttpStatusCode.OK, masterResult.StatusCode);

                var itemResult = await service.GetStoreItemMasters(tenant, store, includeInactive: true);
                Assert.Equal(HttpStatusCode.OK, itemResult.StatusCode);
            }

            await using (var verifyContext = _fixture.CreateDbContext())
            {
                var masters = await verifyContext.StoreServiceMasters
                    .Where(x => x.TenantName == tenant && x.StoreCode == store)
                    .ToListAsync();

                var items = await verifyContext.StoreItemMasters
                    .Where(x => x.TenantName == tenant && x.StoreCode == store)
                    .ToListAsync();

                Assert.NotEmpty(masters.Where(x => string.Equals(x.MasterType, "Service", StringComparison.OrdinalIgnoreCase)));
                Assert.NotEmpty(masters.Where(x => string.Equals(x.MasterType, "Category", StringComparison.OrdinalIgnoreCase)));
                Assert.NotEmpty(items);
            }
        }

        [Fact]
        public async Task StoreMasterInitialization_IsIdempotent_AndDoesNotCreateDuplicates()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using (var context = _fixture.CreateDbContext())
            {
                var service = _fixture.CreateService(context);
                await service.GetStoreServiceMasters(tenant, store, includeInactive: true);
                await service.GetStoreItemMasters(tenant, store, includeInactive: true);
            }

            int masterCountAfterFirstRun;
            int itemCountAfterFirstRun;

            await using (var countContext = _fixture.CreateDbContext())
            {
                masterCountAfterFirstRun = await countContext.StoreServiceMasters
                    .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);

                itemCountAfterFirstRun = await countContext.StoreItemMasters
                    .CountAsync(x => x.TenantName == tenant && x.StoreCode == store);
            }

            await using (var context = _fixture.CreateDbContext())
            {
                var service = _fixture.CreateService(context);
                await service.GetStoreServiceMasters(tenant, store, includeInactive: true);
                await service.GetStoreItemMasters(tenant, store, includeInactive: true);
            }

            await using (var verifyContext = _fixture.CreateDbContext())
            {
                var masters = await verifyContext.StoreServiceMasters
                    .Where(x => x.TenantName == tenant && x.StoreCode == store)
                    .ToListAsync();

                var items = await verifyContext.StoreItemMasters
                    .Where(x => x.TenantName == tenant && x.StoreCode == store)
                    .ToListAsync();

                Assert.Equal(masterCountAfterFirstRun, masters.Count);
                Assert.Equal(itemCountAfterFirstRun, items.Count);

                Assert.Equal(
                    masters.Count,
                    masters.Select(x => $"{x.MasterType}|{x.Name}", StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).Count());

                Assert.Equal(
                    items.Count,
                    items.Select(x => $"{x.ServiceType}|{x.Category}|{x.ItemName}", StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        [Fact]
        public async Task StoreMasterInitialization_PreservesExistingInactiveAndCustomRecords()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            var now = DateTime.UtcNow;

            await using (var setupContext = _fixture.CreateDbContext())
            {
                setupContext.StoreServiceMasters.Add(new StoreServiceMasterEntity
                {
                    TenantName = tenant,
                    StoreCode = store,
                    MasterType = "Service",
                    Name = "Dry Clean",
                    Description = "Existing inactive service",
                    IsActive = false,
                    CreatedDate = now,
                    ModifiedDate = now
                });

                setupContext.StoreServiceMasters.Add(new StoreServiceMasterEntity
                {
                    TenantName = tenant,
                    StoreCode = store,
                    MasterType = "Service",
                    Name = "Custom Service",
                    Description = "Custom inactive service",
                    IsActive = false,
                    CreatedDate = now,
                    ModifiedDate = now
                });

                setupContext.StoreItemMasters.Add(new StoreItemMasterEntity
                {
                    TenantName = tenant,
                    StoreCode = store,
                    ServiceType = "Dry Clean",
                    Category = "Men",
                    ItemName = "Shirt",
                    Description = "Existing inactive item",
                    IsActive = false,
                    CreatedDate = now,
                    ModifiedDate = now
                });

                await setupContext.SaveChangesAsync();
            }

            await using (var context = _fixture.CreateDbContext())
            {
                var service = _fixture.CreateService(context);
                await service.GetStoreServiceMasters(tenant, store, includeInactive: true);
                await service.GetStoreItemMasters(tenant, store, includeInactive: true);
            }

            await using (var verifyContext = _fixture.CreateDbContext())
            {
                var dryCleanServices = await verifyContext.StoreServiceMasters
                    .Where(x => x.TenantName == tenant
                                && x.StoreCode == store
                                && x.MasterType == "Service"
                                && x.Name == "Dry Clean")
                    .ToListAsync();

                Assert.Single(dryCleanServices);
                Assert.False(dryCleanServices[0].IsActive);

                var customService = await verifyContext.StoreServiceMasters
                    .SingleOrDefaultAsync(x => x.TenantName == tenant
                                               && x.StoreCode == store
                                               && x.MasterType == "Service"
                                               && x.Name == "Custom Service");

                Assert.NotNull(customService);
                Assert.False(customService.IsActive);

                var dryCleanShirtItems = await verifyContext.StoreItemMasters
                    .Where(x => x.TenantName == tenant
                                && x.StoreCode == store
                                && x.ServiceType == "Dry Clean"
                                && x.Category == "Men"
                                && x.ItemName == "Shirt")
                    .ToListAsync();

                Assert.Single(dryCleanShirtItems);
                Assert.False(dryCleanShirtItems[0].IsActive);
            }
        }

        private void EnsureFixtureReady()
        {
            Assert.True(_fixture.IsAvailable, _fixture.UnavailableReason ?? "SQL Server test fixture unavailable.");
        }

        private static (string Tenant, string Store) NewScope()
            => ($"tenant-{Guid.NewGuid():N}", $"store-{Guid.NewGuid():N}");
    }
}
