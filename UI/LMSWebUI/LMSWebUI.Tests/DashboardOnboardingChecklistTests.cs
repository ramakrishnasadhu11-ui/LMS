using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LMSWebUI.Controllers;
using LMSWebUI.Models;
using LMSWebUI.Models.Customer;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using RestSharp;
using Xunit;

namespace LMSWebUI.Tests
{
    public class DashboardOnboardingChecklistTests
    {
        [Fact]
        public async Task ResolveOnboardingChecklistProgress_LaundryPrices_UsesSelectedTenantStore_AndMarksCompleted_FromWrappedItemsResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                LaundryItemPricesResponse = new List<LaundryItemPriceDto>
                {
                    new LaundryItemPriceDto
                    {
                        TenantName = "tenant@example.com",
                        StoreCode = "S1",
                        ServiceType = "Dry Clean",
                        Category = "Men",
                        ItemName = "Shirt",
                        UnitPrice = 80.00m,
                        IsActive = true
                    }
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "tenant@example.com", "S1");

            Assert.True(progress.IsLaundryPricesCompleted);

            Assert.NotNull(customerApi.LastLaundryItemPricesQuery);
            Assert.Equal("tenant@example.com", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "tenantName"));
            Assert.Equal("S1", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "storeCode"));
            Assert.Equal("false", GetQueryValue(customerApi.LastLaundryItemPricesQuery, "includeInactive"), ignoreCase: true);
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_BarcodeTags_UsesSelectedTenantStore_AndMarksCompleted_FromMasterResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                BarcodeTagSettingsResponse = new JObject
                {
                    ["TenantName"] = "idia.laundry@example.com",
                    ["StoreCode"] = "IDIA01",
                    ["EnableTagging"] = true,
                    ["TagPrefix"] = "IDL",
                    ["NextTagNumber"] = 12,
                    ["TagNumberPadding"] = 4,
                    ["ResetTagNumberYearly"] = false
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsBarcodeTagsCompleted);

            Assert.NotNull(customerApi.LastBarcodeTagSettingsQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastBarcodeTagSettingsQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastBarcodeTagSettingsQuery, "storeCode"));
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_FirstCustomer_UsesSelectedTenantStore_AndMarksCompleted_FromMasterCustomersResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                CustomersResponse = new JObject
                {
                    ["CustomerNames"] = new JArray("Idia Walk-In")
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsFirstCustomerCompleted);

            Assert.NotNull(customerApi.LastCustomersQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastCustomersQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastCustomersQuery, "storeCode"));
            Assert.Equal(string.Empty, GetQueryValue(customerApi.LastCustomersQuery, "eMail"));
        }

        [Fact]
        public async Task ResolveOnboardingChecklistProgress_FirstOrder_UsesSelectedTenantStore_AndMarksCompleted_FromMasterOrdersResponse()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient
            {
                CustomersResponse = new JObject
                {
                    ["CustomerNames"] = new JArray("Idia Walk-In")
                },
                CustomerByNameResponse = new JObject
                {
                    ["CustCode"] = "CUST-001"
                },
                LaundryOrdersResponse = new JObject
                {
                    ["Orders"] = new JArray(
                        new JObject
                        {
                            ["OrderNo"] = "ORD-0001",
                            ["CustCode"] = "CUST-001"
                        })
                }
            };

            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            var progress = await InvokeChecklistProgressAsync(controller, "idia.laundry@example.com", "IDIA01");

            Assert.True(progress.IsFirstOrderCompleted);

            Assert.NotNull(customerApi.LastCustomerByNameQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastCustomerByNameQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastCustomerByNameQuery, "storeCode"));
            Assert.Equal("Idia Walk-In", GetQueryValue(customerApi.LastCustomerByNameQuery, "customerName"));

            Assert.NotNull(customerApi.LastLaundryOrdersQuery);
            Assert.Equal("idia.laundry@example.com", GetQueryValue(customerApi.LastLaundryOrdersQuery, "tenantName"));
            Assert.Equal("IDIA01", GetQueryValue(customerApi.LastLaundryOrdersQuery, "storeCode"));
            Assert.Equal("CUST-001", GetQueryValue(customerApi.LastLaundryOrdersQuery, "custCode"));
        }

        [Fact]
        public async Task Index_SuperAdmin_WithEmptyTenantStore_AllowsDashboardView()
        {
            var loginApi = new InMemoryLoginApiClient();
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "superadmin@example.com", string.Empty, "SuperAdmin");

            var result = await controller.Index();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("Super Admin Dashboard", controller.ViewData["DashboardTitle"]);
            Assert.Equal("SuperAdmin", controller.ViewData["UserRole"]);
            Assert.False((bool)controller.ViewData["CanResumeOnboarding"]);
            Assert.NotNull(viewResult.Model);
        }

        [Fact]
        public async Task Index_TrialActiveTenant_PopulatesTrialExpiryAndRemainingDays_ForDashboardNotification()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "trial-active@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(15).Date.AddHours(10)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "trial-active@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "trial-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Trial", model.SubscriptionStatus);
            Assert.True(model.TrialEndUtc.HasValue);
            Assert.Equal(loginApi.TenantProfileResponse.TrialEndUtc.Value.ToUniversalTime(), model.TrialEndUtc.Value.ToUniversalTime());
            Assert.True(model.TrialDaysRemaining.HasValue);
            var expectedDaysRemaining = (int)Math.Ceiling((loginApi.TenantProfileResponse.TrialEndUtc.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            Assert.Equal(expectedDaysRemaining, model.TrialDaysRemaining.Value);
        }

        [Fact]
        public async Task Index_ActivePaidTenant_DoesNotPopulateTrialNotificationState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "paid-active@example.com",
                    PlanCode = "Premium",
                    SubscriptionStatus = "Active",
                    TrialEndUtc = DateTime.UtcNow.AddDays(10)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "paid-active@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };
            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "paid-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Active", model.SubscriptionStatus);
            Assert.Null(model.TrialEndUtc);
            Assert.Null(model.TrialDaysRemaining);
        }

        [Fact]
        public async Task Index_AdminTrialActiveTenant_SetsTrialModelData_WithExactExpiryAndRemainingDays()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "admin-trial@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(7).Date.AddHours(9)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "admin-trial@example.com",
                            ["StoreCode"] = "UQO",
                            ["IsActive"] = true
                        })
                }
            };

            var customerApi = new InMemoryDashboardCustomerApiClient();
            var config = new ConfigurationBuilder().Build();
            var env = new FakeWebHostEnvironment();
            var controller = new DashboardController(loginApi, customerApi, config, NullLogger<DashboardController>.Instance, env);

            AttachSession(controller, "admin-trial@example.com", "UQO", "Admin");

            var result = await controller.Index(skipOnboardingGate: true);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(viewResult.Model);
            Assert.Equal("Trial", model.SubscriptionStatus);
            Assert.True(model.TrialEndUtc.HasValue);
            Assert.True(model.TrialDaysRemaining.HasValue);
            Assert.True(model.TrialDaysRemaining.Value <= 7 && model.TrialDaysRemaining.Value >= 6);

        }

        [Fact]
        public async Task Index_TrialActiveTenant_SetsSubscriptionBillingMessage_ForReminderThreshold15()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "trial15@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(15).Date.AddHours(9)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "trial15@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "trial15@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal("Trial", model.SubscriptionStatus);
            Assert.Equal("trial-warning", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains("15", Convert.ToString(controller.ViewData["SubscriptionBillingMessage"]), StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(7)]
        [InlineData(3)]
        [InlineData(1)]
        public async Task Index_TrialActiveTenant_SetsSubscriptionBillingMessages_ForReminderThresholds(int daysRemaining)
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "trial-threshold@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(daysRemaining).Date.AddHours(9)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "trial-threshold@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "trial-threshold@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal("trial-warning", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains($"{daysRemaining}", Convert.ToString(controller.ViewData["SubscriptionBillingMessage"]), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Index_TrialActiveTenant_Expired_SetsSubscriptionBillingUpgradeMessageState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "trial-expired@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(-1)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "trial-expired@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "trial-expired@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal("Expired", model.SubscriptionStatus);
            Assert.Equal("trial-expired", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains("ended", Convert.ToString(controller.ViewData["SubscriptionBillingMessage"]), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Index_ActiveTenant_DoesNotShowTrialWarningSubscriptionBillingState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "paid-active@example.com",
                    PlanCode = "Premium",
                    SubscriptionStatus = "Active",
                    TrialEndUtc = DateTime.UtcNow.AddDays(10)
                },
                StoreStatusesResponse = new JObject
                {
                    ["StoreStatuses"] = new JArray(
                        new JObject
                        {
                            ["TenantName"] = "paid-active@example.com",
                            ["StoreCode"] = "S1",
                            ["IsActive"] = true
                        })
                }
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "paid-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal("paid-active", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
        }

        [Fact]
        public async Task Index_TrialActiveTenant_SubscriptionBillingCard_TrialActiveState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-trial-active@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(20)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-trial-active@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-trial-active@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.True(Convert.ToBoolean(controller.ViewData["ShowSubscriptionBillingCard"]));
            Assert.Equal("trial-active", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.True(Convert.ToBoolean(controller.ViewData["SubscriptionBillingShowTrialProgress"]));
        }

        [Fact]
        public async Task Index_TenantAdmin_SubscriptionBillingCard_AllowsManagementActions()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-admin@example.com",
                    PlanCode = "Premium",
                    SubscriptionStatus = "Active",
                    TrialEndUtc = DateTime.UtcNow.AddDays(5)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-admin@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-admin@example.com", "S1", "Admin");

            await controller.Index(skipOnboardingGate: true);

            Assert.True(Convert.ToBoolean(controller.ViewData["ShowSubscriptionBillingCard"]));
            Assert.True(Convert.ToBoolean(controller.ViewData["SubscriptionBillingCanManageActions"]));
        }

        [Fact]
        public async Task Index_StoreUser_SubscriptionBillingCard_IsReadOnly()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-storeuser@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(10)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-storeuser@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-storeuser@example.com", "S1", "StoreUser");

            await controller.Index(skipOnboardingGate: true);

            Assert.True(Convert.ToBoolean(controller.ViewData["ShowSubscriptionBillingCard"]));
            Assert.False(Convert.ToBoolean(controller.ViewData["SubscriptionBillingCanManageActions"]));
        }

        [Fact]
        public async Task Index_SuperAdmin_SubscriptionBillingCard_RemainsHidden()
        {
            var controller = new DashboardController(new InMemoryLoginApiClient(), new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "superadmin@example.com", string.Empty, "SuperAdmin");

            await controller.Index(skipOnboardingGate: true);

            Assert.False(Convert.ToBoolean(controller.ViewData["ShowSubscriptionBillingCard"]));
            Assert.False(Convert.ToBoolean(controller.ViewData["SubscriptionBillingCanManageActions"]));
        }

        [Fact]
        public async Task Index_TrialActiveTenant_SubscriptionBillingCard_TrialEndingSoonState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-trial-warning@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(7)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-trial-warning@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-trial-warning@example.com", "S1", "StoreUser");

            await controller.Index(skipOnboardingGate: true);

            Assert.Equal("trial-warning", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains("7", Convert.ToString(controller.ViewData["SubscriptionBillingMessage"]), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Index_TrialActiveTenant_TrialDaysRemaining_ClampedTo45AndProgressCappedAt100()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-trial-clamp-max@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(200)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-trial-clamp-max@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-trial-clamp-max@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal(45, model.TrialDaysRemaining);
            Assert.Equal(100, Convert.ToInt32(controller.ViewData["SubscriptionBillingTrialProgressPercent"]));
        }

        [Fact]
        public async Task Index_TrialExpiredTenant_TrialDaysRemaining_ClampedToZeroAndProgressNotNegative()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-trial-clamp-min@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(-10)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-trial-clamp-min@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-trial-clamp-min@example.com", "S1", "StoreUser");

            var result = await controller.Index(skipOnboardingGate: true);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LMSWebUI.Models.DashBoard.TenantInfoDto>(view.Model);
            Assert.Equal(0, model.TrialDaysRemaining);
            Assert.Equal(0, Convert.ToInt32(controller.ViewData["SubscriptionBillingTrialProgressPercent"]));
        }

        [Fact]
        public async Task Index_TrialActiveTenant_SubscriptionBillingCard_TrialExpiredState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-trial-expired@example.com",
                    PlanCode = "Standard",
                    SubscriptionStatus = "TrialActive",
                    TrialEndUtc = DateTime.UtcNow.AddDays(-2)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-trial-expired@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-trial-expired@example.com", "S1", "StoreUser");

            await controller.Index(skipOnboardingGate: true);

            Assert.Equal("trial-expired", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains("ended", Convert.ToString(controller.ViewData["SubscriptionBillingMessage"]), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Index_ActiveTenant_SubscriptionBillingCard_PaidActiveState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-paid-active@example.com",
                    PlanCode = "Premium",
                    SubscriptionStatus = "Active",
                    TrialEndUtc = DateTime.UtcNow.AddDays(10)
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-paid-active@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-paid-active@example.com", "S1", "StoreUser");

            await controller.Index(skipOnboardingGate: true);

            Assert.Equal("paid-active", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.False(Convert.ToBoolean(controller.ViewData["SubscriptionBillingShowTrialProgress"]));
        }

        [Fact]
        public async Task Index_MissingSubscriptionData_SubscriptionBillingCard_FallbackState()
        {
            var loginApi = new InMemoryLoginApiClient
            {
                TenantProfileResponse = new LoginIoResponse
                {
                    TenantName = "billing-missing@example.com",
                    PlanCode = null,
                    SubscriptionStatus = null,
                    TrialEndUtc = null
                },
                StoreStatusesResponse = BuildActiveStoreStatuses("billing-missing@example.com", "S1")
            };

            var controller = new DashboardController(loginApi, new InMemoryDashboardCustomerApiClient(), new ConfigurationBuilder().Build(), NullLogger<DashboardController>.Instance, new FakeWebHostEnvironment());
            AttachSession(controller, "billing-missing@example.com", "S1", "StoreUser");

            await controller.Index(skipOnboardingGate: true);

            Assert.Equal("missing", Convert.ToString(controller.ViewData["SubscriptionBillingState"]));
            Assert.Contains("unavailable", Convert.ToString(controller.ViewData["SubscriptionBillingHeadline"]), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DashboardIndex_View_ContainsSubscriptionBillingCardAndUpgradeLinks()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("id=\"dashboardSubscriptionBillingCard\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Subscription &amp; Billing", viewContents, StringComparison.Ordinal);
            Assert.Contains("View Plans & Upgrade", viewContents, StringComparison.Ordinal);
            Assert.Contains("Manage Subscription", viewContents, StringComparison.Ordinal);
            Assert.Contains("Payment History", viewContents, StringComparison.Ordinal);
            Assert.Contains("Renewals", viewContents, StringComparison.Ordinal);
            Assert.Contains("canManageSubscriptionBillingActions", viewContents, StringComparison.Ordinal);
            Assert.Contains("SubscriptionBillingCanManageActions", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_DoesNotRenderStandaloneTrialBanners_WhileBillingCardRemainsVisible()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("id=\"dashboardSubscriptionBillingCard\"", viewContents, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"dashboardTrialNotification\"", viewContents, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"dashboardTrialStatusBanner\"", viewContents, StringComparison.Ordinal);
            Assert.DoesNotContain("Your free trial is active", viewContents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Your 45-Day Free Trial Is Active", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_ContainsRefinedAdminHeaderContext_AndNoDuplicateHeaderSubscriptionLink()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("admin-store-dashboard-header", viewContents, StringComparison.Ordinal);
            Assert.Contains("Tenant: <strong>@tenantName</strong>", viewContents, StringComparison.Ordinal);
            Assert.Contains("Store: <strong>@tenantStore</strong>", viewContents, StringComparison.Ordinal);
            Assert.Contains("admin-store-context-badge\">Current Store", viewContents, StringComparison.Ordinal);
            Assert.Contains("Reporting Period: <strong id=\"adminHeaderPeriod\">Month to Date</strong>", viewContents, StringComparison.Ordinal);
            Assert.Contains("Last Refreshed: <strong id=\"adminHeaderLastRefreshed\">--</strong>", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"adminHeaderRefreshStatusChip\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("style=\"display:none;\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Manage Laundry Pricing", viewContents, StringComparison.Ordinal);
            Assert.DoesNotContain(">Subscription Plans<", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_ContainsOperationsCommandCenter_AndCustomerLookupPanels()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("Operations Command Center", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"liveCounterPanel\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcPendingProcessing\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcPendingPaymentAmount\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Today's Payment Summary", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"todayPaymentSummaryPanel\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcCashInHand\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcAmountCollectedToday\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcCashCollectedToday\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcUpiCollectedToday\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcCardCollectedToday\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"lcPaymentSummaryStatus\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Create New Order", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"customerLookupPanel\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Customer Lookup", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"searchOptionCustomer\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"searchOptionInvoice\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"selectedCustomerPanel\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"selectedCustomerEmptyState\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("Select a customer to enable customer-specific actions.", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_WiresPaymentSummaryToLiveCounterApi_WithNaFallbackContracts()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("function formatDashboardCurrencyOrNa(value)", viewContents, StringComparison.Ordinal);
            Assert.Contains("function syncPaymentSummaryFromLiveCounters(response)", viewContents, StringComparison.Ordinal);
            Assert.Contains("response.paymentSummaryAvailable === true", viewContents, StringComparison.Ordinal);
            Assert.Contains("response.paymentSummaryReason", viewContents, StringComparison.Ordinal);
            Assert.Contains("setStoreBoardValue(\"lcCashInHand\", \"N/A\", \"N/A\")", viewContents, StringComparison.Ordinal);
            Assert.Contains("setStoreBoardValue(\"lcAmountCollectedToday\", \"N/A\", \"N/A\")", viewContents, StringComparison.Ordinal);
            Assert.Contains("setStoreBoardValue(\"lcCashCollectedToday\", \"N/A\", \"N/A\")", viewContents, StringComparison.Ordinal);
            Assert.Contains("setStoreBoardValue(\"lcUpiCollectedToday\", \"N/A\", \"N/A\")", viewContents, StringComparison.Ordinal);
            Assert.Contains("setStoreBoardValue(\"lcCardCollectedToday\", \"N/A\", \"N/A\")", viewContents, StringComparison.Ordinal);
            Assert.Contains("syncPaymentSummaryFromLiveCounters(response);", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_ContainsCustomerActionsAndDeleteConfirmationFlow()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("id=\"dashboardCreateOrderBtn\" disabled", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"dashboardEditCustomerBtn\" disabled", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"dashboardDeleteCustomerBtn\" disabled", viewContents, StringComparison.Ordinal);
            Assert.Contains("Delete customer '", viewContents, StringComparison.Ordinal);
            Assert.Contains("This action cannot be undone.", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"dashboardClearCustomerBtn\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"dashboardRefreshCustomerOrdersBtn\" disabled", viewContents, StringComparison.Ordinal);
            Assert.Contains("Type to search. Use ↑/↓ to navigate suggestions and Enter to load customer history.", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"customerSearchGoBtn\">Search</button>", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"invoiceSearchGoBtn\">Search</button>", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_UsesSeparatePendingProcessingCount_ForStoreActionAndLiveCounters()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("setStoreBoardValue(\"actPendingCount\", asNumber(response.pendingProcessingCount), \"0\");", viewContents, StringComparison.Ordinal);
            Assert.Contains("setLiveCounterValue(\"lcPendingProcessing\", response.pendingProcessingCount, \"0\");", viewContents, StringComparison.Ordinal);
            Assert.DoesNotContain("setStoreBoardValue(\"actPendingCount\", asNumber(response.pendingDeliveryCount), \"0\");", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_ContainsRedesignedInvoiceLookupComponent_AndPreservesSearchHooks()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("Find Order or Invoice", viewContents, StringComparison.Ordinal);
            Assert.Contains("invoice-lookup-wrap", viewContents, StringComparison.Ordinal);
            Assert.Contains("invoice-lookup-row", viewContents, StringComparison.Ordinal);
            Assert.Contains("invoice-lookup-icon", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"searchInvoiceText\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("placeholder=\"Enter Order No. / Invoice No.\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"invoiceSearchGoBtn\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("id=\"invoiceSearchMessage\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("invoice-lookup-info-row", viewContents, StringComparison.Ordinal);
            Assert.Contains("Partial match supported. Press Enter to preview invoice details and open workflow.", viewContents, StringComparison.Ordinal);
            Assert.Contains("executeInvoiceSearch", viewContents, StringComparison.Ordinal);
            Assert.Contains("triggerInvoiceReferenceLookup", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_UsesPolishedLookupComponent_ForDefaultVisibleCustomerSearch()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("id=\"searchradio\" class=\"search-input-wrap autocomplete invoice-lookup-wrap\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("<div class=\"lookup-row\">", viewContents, StringComparison.Ordinal);
            Assert.Contains("lookup-search-icon", viewContents, StringComparison.Ordinal);
            Assert.Contains("lookup-filter-btn", viewContents, StringComparison.Ordinal);
            Assert.Contains("Type to search. Use ↑/↓ to navigate suggestions and Enter to load customer history.", viewContents, StringComparison.Ordinal);
            Assert.Contains(".search-hint {", viewContents, StringComparison.Ordinal);
            Assert.Contains("background: #f8fafc;", viewContents, StringComparison.Ordinal);
        }

        [Fact]
        public void DashboardIndex_View_PreservesCustomerReselectionFlow_AfterClearSelection()
        {
            var viewPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LMSWebUI", "Views", "Dashboard", "Index.cshtml"));
            var viewContents = File.ReadAllText(viewPath);

            Assert.Contains("var customerSearchRequestSeq = 0;", viewContents, StringComparison.Ordinal);
            Assert.Contains("var customerSelectionRequestSeq = 0;", viewContents, StringComparison.Ordinal);
            Assert.Contains("var activeCustomerSelectionRequestSeq = 0;", viewContents, StringComparison.Ordinal);
            Assert.Contains("activeCustomerSelectionRequestSeq = ++customerSelectionRequestSeq;", viewContents, StringComparison.Ordinal);
            Assert.Contains("loadDashboardCustomerOrders(value, {", viewContents, StringComparison.Ordinal);
            Assert.Contains("forceReload: source === \"search-go\" || source === \"change-event\"", viewContents, StringComparison.Ordinal);
            Assert.Contains("if (!forceReload && codeKey && customerOrdersByCodeCache[codeKey])", viewContents, StringComparison.Ordinal);
            Assert.Contains("if (searchInput) {", viewContents, StringComparison.Ordinal);
            Assert.Contains("searchInput.dispatchEvent(new Event(\"input\"));", viewContents, StringComparison.Ordinal);
            Assert.Contains("delete customerCodeByNameCache[previousNameKey];", viewContents, StringComparison.Ordinal);
            Assert.Contains("delete customerOrdersByCodeCache[previousCodeKey];", viewContents, StringComparison.Ordinal);
            Assert.Contains("if (searchRequestSeq !== customerSearchRequestSeq)", viewContents, StringComparison.Ordinal);
            Assert.Contains("if (isStaleSelectionRequest())", viewContents, StringComparison.Ordinal);
        }

        private static JObject BuildActiveStoreStatuses(string tenantName, string storeCode)
            => new JObject
            {
                ["StoreStatuses"] = new JArray(
                    new JObject
                    {
                        ["TenantName"] = tenantName,
                        ["StoreCode"] = storeCode,
                        ["IsActive"] = true
                    })
            };

        private static async Task<(bool IsBusinessStoreSettingsCompleted, bool IsLaundryPricesCompleted, bool IsBarcodeTagsCompleted, bool IsFirstCustomerCompleted, bool IsFirstOrderCompleted)> InvokeChecklistProgressAsync(
            DashboardController controller,
            string tenantName,
            string tenantStore)
        {
            var method = typeof(DashboardController).GetMethod(
                "ResolveOnboardingChecklistProgressAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
            var task = method.Invoke(controller, new object[] { tenantName, tenantStore }) as Task<(bool, bool, bool, bool, bool)>;
            Assert.NotNull(task);
            return await task;
        }

        private sealed class InMemoryLoginApiClient : ILoginApiClient
        {
            public LoginIoResponse TenantProfileResponse { get; set; }
            public JObject StoreStatusesResponse { get; set; } = new JObject();

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (typeof(T) == typeof(LoginIoResponse)
                    && string.Equals(requestUrl, "/TenantprofileDetails", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult((T)(object)(TenantProfileResponse ?? new LoginIoResponse()));
                }

                if (typeof(T) == typeof(JObject)
                    && string.Equals(requestUrl, "/GetAllStoreStatuses", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult((T)(object)(StoreStatusesResponse ?? new JObject()));
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());
        }

        private static string GetQueryValue(Dictionary<string, string> query, string key)
        {
            return query.FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private static void AttachSession(Controller controller, string tenantName, string tenantStore, string userRole)
        {
            var httpContext = new DefaultHttpContext();
            var session = new TestSession();

            session.Set("TenantName", Encoding.UTF8.GetBytes(tenantName ?? string.Empty));
            session.Set("TenantStore", Encoding.UTF8.GetBytes(tenantStore ?? string.Empty));
            session.Set("UserRole", Encoding.UTF8.GetBytes(userRole ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(tenantStore))
            {
                session.Set("TenantAllowedStores", Encoding.UTF8.GetBytes(tenantStore));
            }

            httpContext.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        }

        private sealed class TestSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>();

            public bool IsAvailable => true;
            public string Id { get; } = Guid.NewGuid().ToString();
            public IEnumerable<string> Keys => _store.Keys;

            public void Clear() => _store.Clear();

            public Task CommitAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task LoadAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Remove(string key) => _store.Remove(key);

            public void Set(string key, byte[] value) => _store[key] = value;

            public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value);
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public ISession Session { get; set; }
        }

        private sealed class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context)
                => new Dictionary<string, object>();

            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
            }
        }

        private sealed class FakeWebHostEnvironment : IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "LMSWebUI.Tests";
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        }

        private sealed class InMemoryDashboardCustomerApiClient : ICustomerApiClient
        {
            public List<LaundryItemPriceDto> LaundryItemPricesResponse { get; set; } = new List<LaundryItemPriceDto>();
            public Dictionary<string, string> LastLaundryItemPricesQuery { get; private set; }
            public JObject BarcodeTagSettingsResponse { get; set; }
            public Dictionary<string, string> LastBarcodeTagSettingsQuery { get; private set; }
            public JObject CustomersResponse { get; set; }
            public Dictionary<string, string> LastCustomersQuery { get; private set; }
            public JObject CustomerByNameResponse { get; set; }
            public Dictionary<string, string> LastCustomerByNameQuery { get; private set; }
            public JObject LaundryOrdersResponse { get; set; }
            public Dictionary<string, string> LastLaundryOrdersQuery { get; private set; }

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (string.Equals(requestUrl, "/GetLaundryItemPrices", StringComparison.OrdinalIgnoreCase))
                {
                    LastLaundryItemPricesQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(List<LaundryItemPriceDto>))
                    {
                        return Task.FromResult((T)(object)LaundryItemPricesResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetBarcodeTagSettings", StringComparison.OrdinalIgnoreCase))
                {
                    LastBarcodeTagSettingsQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)BarcodeTagSettingsResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetCustomers", StringComparison.OrdinalIgnoreCase))
                {
                    LastCustomersQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)CustomersResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetCustomerByName", StringComparison.OrdinalIgnoreCase))
                {
                    LastCustomerByNameQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)CustomerByNameResponse);
                    }
                }

                if (string.Equals(requestUrl, "/GetLaundryOrders", StringComparison.OrdinalIgnoreCase))
                {
                    LastLaundryOrdersQuery = queryParams != null
                        ? new Dictionary<string, string>(queryParams, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (typeof(T) == typeof(JObject))
                    {
                        return Task.FromResult((T)(object)LaundryOrdersResponse);
                    }
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
                => Task.FromResult(new T());
        }
    }
}
