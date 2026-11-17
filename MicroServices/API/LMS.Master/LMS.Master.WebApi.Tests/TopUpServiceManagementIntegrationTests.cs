using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Services;
using LMS.Master.DTO;
using LMS.Master.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace LMS.Master.WebApi.Tests
{
    public class TopUpServiceManagementIntegrationTests : IClassFixture<RazorpaySqlServerFixture>
    {
        private readonly RazorpaySqlServerFixture _fixture;

        public TopUpServiceManagementIntegrationTests(RazorpaySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SaveTopUpServiceMaster_CreateAndUpdate_PersistsApplicability()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var create = await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Stain Removal",
                    ChargeType = "PerGarment",
                    UnitPrice = 12.5m,
                    IsActive = true,
                    IsArchived = false,
                    Applicability = new List<TopUpServiceApplicabilityDto>
                    {
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = "Men", ItemName = "Shirt", IsActive = true },
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = "Men", ItemName = null, IsActive = true }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            var created = Assert.IsType<SaveTopUpServiceMasterResponseDto>(create.ResultSet);
            Assert.NotNull(created.TopUpService);
            Assert.True(created.TopUpService.Id > 0);
            Assert.Equal(2, created.TopUpService.Applicability.Count);

            var update = await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    Id = created.TopUpService.Id,
                    TopUpName = "Stain Removal",
                    ChargeType = "PerOrder",
                    UnitPrice = 15m,
                    IsActive = true,
                    IsArchived = false,
                    Applicability = new List<TopUpServiceApplicabilityDto>
                    {
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = null, ItemName = null, IsActive = true }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var updated = Assert.IsType<SaveTopUpServiceMasterResponseDto>(update.ResultSet);
            Assert.Equal("PerOrder", updated.TopUpService.ChargeType);
            Assert.Equal(15m, updated.TopUpService.UnitPrice);
            Assert.Single(updated.TopUpService.Applicability);
        }

        [Fact]
        public async Task SaveTopUpServiceMaster_DuplicateActiveName_ReturnsBadRequest()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            var first = await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Premium Fragrance",
                    ChargeType = "PerOrder",
                    UnitPrice = 5m,
                    IsActive = true,
                    IsArchived = false
                }
            });

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var duplicate = await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Premium Fragrance",
                    ChargeType = "PerOrder",
                    UnitPrice = 8m,
                    IsActive = true,
                    IsArchived = false
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            var response = Assert.IsType<CustomerIOResponse>(duplicate.ResultSet);
            Assert.Equal("An active top-up service with the same name already exists for this store.", response.Message);
        }

        [Fact]
        public async Task GetTopUpServiceMasters_IncludeInactive_AndIsolation_WorkAsExpected()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            var otherScope = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Fabric Softener",
                    ChargeType = "PerOrder",
                    UnitPrice = 4m,
                    IsActive = true,
                    IsArchived = false
                }
            });

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Extra Steam Press",
                    ChargeType = "PerGarment",
                    UnitPrice = 3m,
                    IsActive = false,
                    IsArchived = true
                }
            });

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = otherScope.Tenant,
                StoreCode = otherScope.Store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Other Scope TopUp",
                    ChargeType = "PerOrder",
                    UnitPrice = 9m,
                    IsActive = true,
                    IsArchived = false
                }
            });

            var activeOnly = await service.GetTopUpServiceMasters(tenant, store, includeInactive: false);
            Assert.Equal(HttpStatusCode.OK, activeOnly.StatusCode);
            var activeResponse = Assert.IsType<GetTopUpServiceMastersResponseDto>(activeOnly.ResultSet);
            Assert.Single(activeResponse.TopUpServices);
            Assert.Equal("Fabric Softener", activeResponse.TopUpServices[0].TopUpName);

            var allInScope = await service.GetTopUpServiceMasters(tenant, store, includeInactive: true);
            Assert.Equal(HttpStatusCode.OK, allInScope.StatusCode);
            var allResponse = Assert.IsType<GetTopUpServiceMastersResponseDto>(allInScope.ResultSet);
            Assert.Equal(2, allResponse.TopUpServices.Count);
            Assert.DoesNotContain(allResponse.TopUpServices, x => x.TopUpName == "Other Scope TopUp");
        }

        [Fact]
        public async Task SetTopUpServiceStatus_ArchivesAndRestoresWithoutDeletion()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Stain Removal",
                    ChargeType = "PerOrder",
                    UnitPrice = 6m,
                    IsActive = true,
                    IsArchived = false
                }
            });

            var archive = await service.SetTopUpServiceStatus(new SetTopUpServiceStatusRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpName = "Stain Removal",
                IsActive = false,
                IsArchived = true
            });

            Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
            var archivedResponse = Assert.IsType<SetTopUpServiceStatusResponseDto>(archive.ResultSet);
            Assert.True(archivedResponse.TopUpService.IsArchived);
            Assert.False(archivedResponse.TopUpService.IsActive);

            var hiddenFromActive = await service.GetTopUpServiceMasters(tenant, store, includeInactive: false);
            var activeList = Assert.IsType<GetTopUpServiceMastersResponseDto>(hiddenFromActive.ResultSet);
            Assert.Empty(activeList.TopUpServices);

            var restore = await service.SetTopUpServiceStatus(new SetTopUpServiceStatusRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpName = "Stain Removal",
                IsActive = true,
                IsArchived = false
            });

            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            var restoredResponse = Assert.IsType<SetTopUpServiceStatusResponseDto>(restore.ResultSet);
            Assert.False(restoredResponse.TopUpService.IsArchived);
            Assert.True(restoredResponse.TopUpService.IsActive);

            var visibleAgain = await service.GetTopUpServiceMasters(tenant, store, includeInactive: false);
            var visibleList = Assert.IsType<GetTopUpServiceMastersResponseDto>(visibleAgain.ResultSet);
            Assert.Single(visibleList.TopUpServices);
        }

        [Fact]
        public async Task GetApplicableTopUps_UsesPrecedence_AndTenantStoreIsolation()
        {
            EnsureFixtureReady();
            var (tenant, store) = NewScope();
            var otherScope = NewScope();

            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateService(context);

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Spot Fix",
                    ChargeType = "PerGarment",
                    UnitPrice = 11m,
                    IsActive = true,
                    IsArchived = false,
                    Applicability = new List<TopUpServiceApplicabilityDto>
                    {
                        new TopUpServiceApplicabilityDto { ServiceType = null, Category = null, ItemName = null, IsActive = true },
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = null, ItemName = null, IsActive = true },
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = "Men", ItemName = null, IsActive = true },
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = "Men", ItemName = "Shirt", IsActive = true }
                    }
                }
            });

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Premium Fragrance",
                    ChargeType = "PerOrder",
                    UnitPrice = 4m,
                    IsActive = true,
                    IsArchived = false,
                    Applicability = new List<TopUpServiceApplicabilityDto>
                    {
                        new TopUpServiceApplicabilityDto { ServiceType = null, Category = null, ItemName = null, IsActive = true }
                    }
                }
            });

            await service.SaveTopUpServiceMaster(new SaveTopUpServiceMasterRequestDto
            {
                TenantName = otherScope.Tenant,
                StoreCode = otherScope.Store,
                TopUpService = new TopUpServiceMasterDto
                {
                    TopUpName = "Foreign Scope Service",
                    ChargeType = "PerOrder",
                    UnitPrice = 8m,
                    IsActive = true,
                    IsArchived = false,
                    Applicability = new List<TopUpServiceApplicabilityDto>
                    {
                        new TopUpServiceApplicabilityDto { ServiceType = "Dry Clean", Category = "Men", ItemName = "Shirt", IsActive = true }
                    }
                }
            });

            var exact = await service.GetApplicableTopUps(new GetApplicableTopUpsRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                ServiceType = "Dry Clean",
                Category = "Men",
                ItemName = "Shirt",
                OrderMode = "pieces"
            });

            Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
            var exactResponse = Assert.IsType<GetApplicableTopUpsResponseDto>(exact.ResultSet);
            Assert.Equal(2, exactResponse.TopUpServices.Count);
            Assert.DoesNotContain(exactResponse.TopUpServices, x => x.TopUpName == "Foreign Scope Service");

            var spotFixExact = exactResponse.TopUpServices.Single(x => x.TopUpName == "Spot Fix");
            var exactRule = Assert.Single(spotFixExact.Applicability);
            Assert.Equal("Dry Clean", exactRule.ServiceType);
            Assert.Equal("Men", exactRule.Category);
            Assert.Equal("Shirt", exactRule.ItemName);

            var categoryFallback = await service.GetApplicableTopUps(new GetApplicableTopUpsRequestDto
            {
                TenantName = tenant,
                StoreCode = store,
                ServiceType = "Dry Clean",
                Category = "Men",
                ItemName = "Trouser",
                OrderMode = "pieces"
            });

            Assert.Equal(HttpStatusCode.OK, categoryFallback.StatusCode);
            var fallbackResponse = Assert.IsType<GetApplicableTopUpsResponseDto>(categoryFallback.ResultSet);
            var spotFixCategory = fallbackResponse.TopUpServices.Single(x => x.TopUpName == "Spot Fix");
            var categoryRule = Assert.Single(spotFixCategory.Applicability);
            Assert.Equal("Dry Clean", categoryRule.ServiceType);
            Assert.Equal("Men", categoryRule.Category);
            Assert.Null(categoryRule.ItemName);
        }

        private void EnsureFixtureReady()
        {
            Assert.True(_fixture.IsAvailable, _fixture.UnavailableReason ?? "SQL Server test fixture unavailable.");
        }

        private static (string Tenant, string Store) NewScope()
            => ($"tenant-{Guid.NewGuid():N}", $"store-{Guid.NewGuid():N}");
    }
}
