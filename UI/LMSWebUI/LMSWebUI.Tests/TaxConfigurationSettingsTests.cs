using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LMSClientFactory.Helper;
using LMSWebUI.Controllers;
using LMSWebUI.Data;
using LMSWebUI.Models;
using LMSWebUI.Models.Admin;
using LMSWebUI.Models.Customer;
using LMSWebUI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Jint;
using Xunit;

namespace LMSWebUI.Tests
{
    public class TaxConfigurationSettingsTests
    {
        [Fact]
        public async Task TaxInvoiceSettings_Get_AutoCreatesDefaultConfiguration_WhenMissing()
        {
            var customerApi = new InMemoryCustomerApiClient();
            customerApi.GetTaxConfigurationResponses.Enqueue(null);
            customerApi.ResolveTaxModeResponse = new TaxModeResolutionDto { TaxMode = "VAT" };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry Services",
                    CountryCode = "AE",
                    CurrencyCode = "AED"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "DXB1", "Admin");

            var result = await controller.TaxInvoiceSettings();

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.True(model.IsAutoCreatedDefault);
            Assert.Equal("VAT", model.TaxMode);
            Assert.Equal("AE", model.CountryCode);
            Assert.Equal("AED", model.CurrencyCode);
            Assert.Equal("EXCEL-DXB1-", model.InvoicePrefix);
            Assert.Equal("VAT", model.ResolvedTaxMode);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task HttpClientApi_GetTaxConfiguration_404WithJsonBody_ReturnsNull()
        {
            var port = GetFreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            var handler = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 404;
                context.Response.ContentType = "application/json";
                var payload = "{\"StatusCode\":\"404\",\"Message\":\"Tax configuration not found.\"}";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
            });

            var api = new HttpClientApi($"http://localhost:{port}/");
            var result = await api.SendRequestAsync<TaxConfigurationDto>(
                "/GetTaxConfiguration",
                new Dictionary<string, string>
                {
                    ["tenantName"] = "tenant@example.com",
                    ["storeCode"] = "S1"
                },
                Method.GET);

            await handler;

            Assert.Null(result);
        }

        [Fact]
        public async Task HttpClientApi_NonTaxConfiguration404WithJsonBody_PreservesExistingBehavior()
        {
            var port = GetFreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            var handler = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                context.Response.StatusCode = 404;
                context.Response.ContentType = "application/json";
                var payload = "{\"TenantName\":\"404\",\"StoreCode\":\"S1\"}";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                context.Response.Close();
            });

            var api = new HttpClientApi($"http://localhost:{port}/");
            var result = await api.SendRequestAsync<TaxConfigurationDto>(
                "/SomeOtherEndpoint",
                new Dictionary<string, string>
                {
                    ["tenantName"] = "tenant@example.com",
                    ["storeCode"] = "S1"
                },
                Method.GET);

            await handler;

            Assert.NotNull(result);
            Assert.Equal("404", result.TenantName);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_DoesNotAutoCreateIndiaGstComponents_WhenMissingComponents()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN01",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18m,
                    Components = new List<TaxInvoiceComponentDto>()
                }

            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry India",
                    CountryCode = "IN",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "IN01", "Admin");

            var result = await controller.TaxInvoiceSettings();

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Empty(customerApi.AddComponentRequests);
            Assert.Equal(18m, model.GstPercent);
            Assert.Empty(model.Components);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_DoesNotOverwriteExistingComponents()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN02",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18m,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 71, Code = "CGST", DisplayName = "Admin CGST", RatePercent = 8.25m, Priority = 1, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 72, Code = "SGST", DisplayName = "Admin SGST", RatePercent = 8.75m, Priority = 2, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "IN02", "Admin");

            var result = await controller.TaxInvoiceSettings();

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Empty(customerApi.AddComponentRequests);
            Assert.Equal(17m, model.GstPercent);
            Assert.Contains(model.Components, c => c.Code == "CGST" && c.RatePercent == 8.25m);
            Assert.Contains(model.Components, c => c.Code == "SGST" && c.RatePercent == 8.75m);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_LoadsExistingIndiaGstSplitRates()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN04",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18m,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 81, Code = "CGST", DisplayName = "Central GST", RatePercent = 9m, Priority = 1, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 82, Code = "SGST", DisplayName = "State GST", RatePercent = 9m, Priority = 2, IsActive = true }
                    }
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry India",
                    CountryCode = "IN",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "IN04", "Admin");

            var result = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Equal(9m, model.CgstRatePercent);
            Assert.Equal(9m, model.SgstRatePercent);
            Assert.Equal(18m, (model.CgstRatePercent ?? 0m) + (model.SgstRatePercent ?? 0m));
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_UsesComponentSplitAsTotalGst_WhenStoredGstPercentIsZero()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN07",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 0m,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 91, Code = "CGST", DisplayName = "Central GST", RatePercent = 9m, Priority = 1, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 92, Code = "SGST", DisplayName = "State GST", RatePercent = 9m, Priority = 2, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "IN07", "Admin");

            var result = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Equal(18m, model.GstPercent);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_MissingIndiaGstSplitRates_UsesSafeInitialState()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN05",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18m,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "IN05", "Admin");

            var result = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Null(model.CgstRatePercent);
            Assert.Null(model.SgstRatePercent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_IndiaGstUiFallbackAndVisibility_AreConfigured()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("var initialCgstRate = Model.CgstRatePercent ?? (isIndiaGstSelection ? fallbackHalfGst : (decimal?)null);", viewContent);
            Assert.Contains("var initialSgstRate = Model.SgstRatePercent ?? (isIndiaGstSelection ? fallbackHalfGst : (decimal?)null);", viewContent);
            Assert.Contains("var initialTotalGstRate = isIndiaGstSelection && hasBothIndiaGstComponents", viewContent);
            Assert.Contains("var isIndiaGst = mode === 'gst' && country === 'IN';", viewContent);
            Assert.Contains("gstRateConfiguration.style.display = isIndiaGst ? 'block' : 'none';", viewContent);
            Assert.Contains("gstSplitSummary.style.display = isIndiaGst ? 'block' : 'none';", viewContent);
            Assert.Contains("if (gstSplitSummary && isIndiaGst)", viewContent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_UkVat_DoesNotRenderIndiaGstAsDefault()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("var isVatSelection = string.Equals(Model.TaxMode, \"VAT\", StringComparison.OrdinalIgnoreCase);", viewContent);
            Assert.Contains("var initialTaxRateLabel = isVatSelection", viewContent);
            Assert.Contains("? \"VAT Rate (%)\"", viewContent);
            Assert.Contains("id=\"gstSplitSummary\" class=\"tax-inline-validation\" style=\"display:@(isIndiaGstSelection ? \"block\" : \"none\")\"", viewContent);
            Assert.Contains("if (gstSplitSummary && isIndiaGst)", viewContent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_UsSalesTax_UsesSalesTaxLabeling()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("var isSalesTaxSelection = string.Equals(Model.TaxMode, \"SalesTax\", StringComparison.OrdinalIgnoreCase);", viewContent);
            Assert.Contains("? \"Sales Tax Rate (%)\"", viewContent);
            Assert.Contains("? 'Sales Tax Rate (%)'", viewContent);
            Assert.Contains("Set the Sales Tax percentage to apply on customer invoices.", viewContent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_NoTax_HidesRateConfiguration()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("var isNoTaxSelection = string.Equals(Model.TaxMode, \"NoTax\", StringComparison.OrdinalIgnoreCase);", viewContent);
            Assert.Contains("id=\"taxRateConfiguration\" style=\"display:@(isNoTaxSelection ? \"none\" : \"block\")\"", viewContent);
            Assert.Contains("taxRateConfiguration.style.display = isNoTax ? 'none' : 'block';", viewContent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_ModeSwitch_RefreshesTaxRateUi()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("function refreshTaxRateConfigurationUi()", viewContent);
            Assert.Contains("taxMode.addEventListener('change', function ()", viewContent);
            Assert.Contains("validateGstSplit();", viewContent);
            Assert.Contains("countryCodeInput.addEventListener('input', function ()", viewContent);
            Assert.Contains("refreshTaxRateConfigurationUi();", viewContent);
        }

        [Fact]
        public void TaxInvoiceSettings_View_ToastFallback_IsConfigured()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TaxInvoiceSettings.cshtml"));

            var viewContent = File.ReadAllText(viewPath);

            Assert.Contains("id=\"taxSettingsInlineToast\"", viewContent);
            Assert.Contains("if (typeof window.AlertMessage === 'function')", viewContent);
            Assert.Contains("inlineToast.className = 'tax-inline-toast alert ' + (toastIsSuccess ? 'alert-success' : 'alert-danger');", viewContent);
            Assert.Contains("if (wasDisplayed)", viewContent);
            Assert.Contains("window.taxSettingsToastMessage = null;", viewContent);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_AllowsIndependentComponentRateEditing()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "IN03", "Admin");

            await controller.SaveTaxComponent(new TaxConfigurationViewModel
            {
                ComponentEditor = new TaxComponentEditorViewModel
                {
                    IsEdit = true,
                    ComponentId = 90,
                    Code = "CGST",
                    DisplayName = "Central GST",
                    RatePercent = 8.5m,
                    Priority = 1,
                    IsActive = true
                }
            });

            await controller.SaveTaxComponent(new TaxConfigurationViewModel
            {
                ComponentEditor = new TaxComponentEditorViewModel
                {
                    IsEdit = true,
                    ComponentId = 91,
                    Code = "SGST",
                    DisplayName = "State GST",
                    RatePercent = 9.5m,
                    Priority = 2,
                    IsActive = true
                }
            });

            Assert.Equal(2, customerApi.UpdateComponentRequests.Count);
            Assert.Contains(customerApi.UpdateComponentRequests, c => c.Code == "CGST" && c.RatePercent == 8.5m);
            Assert.Contains(customerApi.UpdateComponentRequests, c => c.Code == "SGST" && c.RatePercent == 9.5m);
        }

        [Fact]
        public async Task LoadTaxConfigurationScreenModel_MapsConfiguration_AndSortsComponents()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    IsPricesInclusive = true,
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 55,
                    InvoiceNumberPadding = 6,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 2, Code = "SGST", DisplayName = "SGST", RatePercent = 9, Priority = 2, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 1, Code = "CGST", DisplayName = "CGST", RatePercent = 9, Priority = 1, IsActive = true }
                    }
                },
                ResolveTaxModeResponse = new TaxModeResolutionDto { TaxMode = "SalesTax" }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry Services", CountryCode = "IN", CurrencyCode = "INR" }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var model = await InvokePrivateAsync<TaxConfigurationViewModel>(
                controller,
                "LoadTaxConfigurationScreenModelAsync",
                "tenant@example.com",
                "S1",
                false);

            Assert.NotNull(model);
            Assert.Equal("SalesTax", model.ResolvedTaxMode);
            Assert.Equal(2, model.Components.Count);
            Assert.Equal("CGST", model.Components[0].Code);
            Assert.Equal("SGST", model.Components[1].Code);
            Assert.Equal(3, model.ComponentEditor.Priority);
        }

        [Fact]
        public async Task BuildDefaultTaxConfigurationRequest_ResolvesCountryCurrencyAndTaxMode()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Northwind Laundry",
                    CountryCode = "US",
                    CurrencyCode = null
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "STORE99", "Admin");

            var request = await InvokePrivateAsync<TaxConfigurationUpsertRequestDto>(
                controller,
                "BuildDefaultTaxConfigurationRequestAsync",
                "tenant@example.com",
                "STORE99",
                "Northwind Laundry");

            Assert.Equal("SalesTax", request.TaxMode);
            Assert.Equal("US", request.CountryCode);
            Assert.Equal("USD", request.CurrencyCode);
            Assert.True(request.EnableTax);
            Assert.Equal(18m, request.GstPercent);
            Assert.Equal("NORTH-STORE9-", request.InvoicePrefix);
        }

        [Fact]
        public void ValidateTaxConfigurationViewModel_HandlesTaxModeAndValidationFailures()
        {
            var valid = new TaxConfigurationViewModel
            {
                TaxMode = "NoTax",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = false,
                GstPercent = 0,
                InvoicePrefix = "INV-"
            };

            var validResult = InvokePrivateStaticWithOut<bool>(
                typeof(CustomerController),
                "ValidateTaxConfigurationViewModel",
                valid,
                out var validError);

            Assert.True(validResult);
            Assert.Null(validError);

            var invalid = new TaxConfigurationViewModel
            {
                TaxMode = "BadMode",
                CountryCode = "IND",
                CurrencyCode = "",
                EnableTax = true,
                GstPercent = 0,
                InvoicePrefix = "INV_*"
            };

            var invalidResult = InvokePrivateStaticWithOut<bool>(
                typeof(CustomerController),
                "ValidateTaxConfigurationViewModel",
                invalid,
                out var invalidError);

            Assert.False(invalidResult);
            Assert.False(string.IsNullOrWhiteSpace(invalidError));
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_InvalidModel_ReturnsView_AndSkipsSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };
            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var model = new TaxConfigurationViewModel
            {
                TaxMode = "INVALID",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4
            };

            var result = await controller.TaxInvoiceSettings(model);

            var view = Assert.IsType<ViewResult>(result);
            Assert.IsType<TaxConfigurationViewModel>(view.Model);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);
            Assert.Contains(customerApi.Calls, c => c.RequestUrl == "/GetTaxConfiguration");
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_SucceedsWithoutComponentEditorFields()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 71, Code = "CGST", RatePercent = 9m, IsActive = true, Priority = 1, ConcurrencyToken = "cgst-71" },
                        new TaxInvoiceComponentDto { Id = 72, Code = "SGST", RatePercent = 9m, IsActive = true, Priority = 2, ConcurrencyToken = "sgst-72" }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 20,
                CgstRatePercent = 10,
                SgstRatePercent = 10,
                InvoicePrefix = "IDIAL-XRE-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_PopulatesCountryRecommendation_WithoutChangingSelection()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "SalesTax",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry",
                    CountryCode = "IN",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Equal("SalesTax", model.TaxMode);
            Assert.Equal("GST", model.RecommendedTaxMode);
            Assert.Equal("IN", model.TenantRegisteredCountryCode);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Get_MapsCountryNameToCountryCode_WhenProfileCountryCodeMissing()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry",
                    CountryCode = null,
                    Country = "India",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);

            Assert.Equal("IN", model.TenantRegisteredCountryCode);
            Assert.Equal("GST", model.RecommendedTaxMode);
            Assert.Equal("IN", model.CountryCode);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_UsesTenantProfileCountryCurrency_WhenHiddenFieldsAreNotEditable()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry",
                    CountryCode = "IN",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                TaxModeOverrideConfirmed = true,
                EnableTax = true,
                GstPercent = 18,
                CountryCode = string.Empty,
                CurrencyCode = string.Empty,
                IsPricesInclusive = false,
                PricesIncludeTax = true,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 5,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                Notes = ""
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(customerApi.LastUpsertTaxConfigurationRequest);
            Assert.Equal("IN", customerApi.LastUpsertTaxConfigurationRequest.CountryCode);
            Assert.Equal("INR", customerApi.LastUpsertTaxConfigurationRequest.CurrencyCode);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_PreservesExistingComponents_WhenSavingSimplifiedPage()
        {
            var existingComponents = new List<TaxInvoiceComponentDto>
            {
                new TaxInvoiceComponentDto { Id = 301, Code = "CGST", DisplayName = "Central GST", RatePercent = 9m, Priority = 1, IsActive = true },
                new TaxInvoiceComponentDto { Id = 302, Code = "SGST", DisplayName = "State GST", RatePercent = 9m, Priority = 2, IsActive = true }
            };

            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = existingComponents
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Excel Laundry",
                    CountryCode = "IN",
                    CurrencyCode = "INR"
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                TaxModeOverrideConfirmed = true,
                EnableTax = true,
                GstPercent = 18,
                CountryCode = "IN",
                CurrencyCode = "INR",
                IsPricesInclusive = false,
                PricesIncludeTax = true,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 9,
                InvoiceNumberPadding = 4,
                ResetInvoiceNumberYearly = false,
                Notes = ""
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(customerApi.AddComponentRequests);
            Assert.Empty(customerApi.UpdateComponentRequests);
            Assert.NotNull(customerApi.StoredTaxConfiguration);
            Assert.NotNull(customerApi.StoredTaxConfiguration.Components);
            Assert.Equal(2, customerApi.StoredTaxConfiguration.Components.Count);
            Assert.Contains(customerApi.StoredTaxConfiguration.Components, c => c.Id == 301 && c.Code == "CGST" && c.RatePercent == 9m);
            Assert.Contains(customerApi.StoredTaxConfiguration.Components, c => c.Id == 302 && c.Code == "SGST" && c.RatePercent == 9m);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_GstSplitValidRates_AllowsSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 11, Code = "CGST", RatePercent = 9m, IsActive = true, Jurisdiction = "IN", Priority = 1, DisplayName = "Central GST", ConcurrencyToken = "cgst-v1" },
                        new TaxInvoiceComponentDto { Id = 12, Code = "SGST", RatePercent = 9m, IsActive = true, Jurisdiction = "IN", Priority = 2, DisplayName = "State GST", ConcurrencyToken = "sgst-v1" }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 8.5m,
                SgstRatePercent = 9.5m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
            Assert.Equal(2, customerApi.UpdateComponentRequests.Count);
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "CGST" && x.ComponentId == 11 && x.RatePercent == 8.5m && x.ConcurrencyToken == "cgst-v1");
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "SGST" && x.ComponentId == 12 && x.RatePercent == 9.5m && x.ConcurrencyToken == "sgst-v1");
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_GstSplitEqualToTotal_AllowsSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 21, Code = "CGST", RatePercent = 9m, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 22, Code = "SGST", RatePercent = 9m, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 9m,
                SgstRatePercent = 9m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_EnableTaxFalse_PersistsGstAndCreatesMissingIndiaComponents()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = false,
                    GstPercent = 0,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var postResult = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = false,
                GstPercent = 18m,
                CgstRatePercent = 9m,
                SgstRatePercent = 9m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(postResult);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
            Assert.NotNull(customerApi.LastUpsertTaxConfigurationRequest);
            Assert.False(customerApi.LastUpsertTaxConfigurationRequest.EnableTax ?? true);
            Assert.Equal(18m, customerApi.LastUpsertTaxConfigurationRequest.GstPercent);
            Assert.Equal(2, customerApi.AddComponentRequests.Count);
            Assert.Contains(customerApi.AddComponentRequests, x => x.Code == "CGST" && x.RatePercent == 9m);
            Assert.Contains(customerApi.AddComponentRequests, x => x.Code == "SGST" && x.RatePercent == 9m);

            var getResult = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(getResult);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);
            Assert.False(model.EnableTax);
            Assert.Equal(18m, model.GstPercent);
            Assert.Equal(9m, model.CgstRatePercent);
            Assert.Equal(9m, model.SgstRatePercent);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_ReenableTax_RetainsSavedIndiaRates()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = false,
                    GstPercent = 18m,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 41, Code = "CGST", RatePercent = 9m, IsActive = true, Priority = 1, ConcurrencyToken = "cgst-41" },
                        new TaxInvoiceComponentDto { Id = 42, Code = "SGST", RatePercent = 9m, IsActive = true, Priority = 2, ConcurrencyToken = "sgst-42" }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var postResult = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18m,
                CgstRatePercent = 9m,
                SgstRatePercent = 9m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(postResult);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
            Assert.True(customerApi.LastUpsertTaxConfigurationRequest.EnableTax ?? false);
            Assert.Equal(18m, customerApi.LastUpsertTaxConfigurationRequest.GstPercent);
            Assert.Equal(2, customerApi.UpdateComponentRequests.Count);
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "CGST" && x.ComponentId == 41 && x.ConcurrencyToken == "cgst-41");
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "SGST" && x.ComponentId == 42 && x.ConcurrencyToken == "sgst-42");

            var getResult = await controller.TaxInvoiceSettings();
            var view = Assert.IsType<ViewResult>(getResult);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);
            Assert.Equal(18m, model.GstPercent);
            Assert.Equal(9m, model.CgstRatePercent);
            Assert.Equal(9m, model.SgstRatePercent);
        }

        [Fact]
        public void SetupCreateOrder_TaxApplication_RemainsGatedByEnableTaxFlag()
        {
            var controllerPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Controllers",
                "CustomerController.cs"));

            var content = File.ReadAllText(controllerPath);
            Assert.Contains("var taxEnabled = taxSettings.EnableTax && taxSettings.GstPercent > 0;", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupCreateOrder_View_NewOrderDefaultsPaidNowToZero()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);

            Assert.Contains("if (paidNowInput && !((paidNowInput.value || \"\").trim()))", content, StringComparison.Ordinal);
            Assert.Contains("paidNowInput.value = \"0.00\";", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupCreateOrder_View_FullAndPartialManualPayment_ArePreservedAndClamped()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);

            Assert.Contains("if (paidNow > netBeforePayment)", content, StringComparison.Ordinal);
            Assert.Contains("paidNowInput.value = netBeforePayment.toFixed(2);", content, StringComparison.Ordinal);
            Assert.Contains("var pendingAmount = netBeforePayment - paidNow;", content, StringComparison.Ordinal);
            Assert.Contains("if (paidNow < 0)", content, StringComparison.Ordinal);
            Assert.Contains("paidNowInput.value = \"0.00\";", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupCreateOrder_View_AdvanceUsedWithZeroPayment_ComputesPendingFromNetBeforePayment()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);

            Assert.Contains("var netBeforePayment = breakup.total - advanceUsed;", content, StringComparison.Ordinal);
            Assert.Contains("if (netBeforePayment < 0) netBeforePayment = 0;", content, StringComparison.Ordinal);
            Assert.Contains("var pendingAmount = netBeforePayment - paidNow;", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupCreateOrder_View_TaxOrOrderAmountChanges_RecalculatePendingAfterManualPayment()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);

            Assert.Contains("function syncTotals()", content, StringComparison.Ordinal);
            Assert.Contains("document.getElementById(\"advanceUsed\").addEventListener(\"input\", syncTotals);", content, StringComparison.Ordinal);
            Assert.Contains("document.getElementById(\"paidNow\").addEventListener(\"input\", syncTotals);", content, StringComparison.Ordinal);
            Assert.Contains("document.getElementById(\"weightInKg\").addEventListener(\"input\", syncTotals);", content, StringComparison.Ordinal);
            Assert.Contains("document.getElementById(\"ratePerKg\").addEventListener(\"input\", syncTotals);", content, StringComparison.Ordinal);
            Assert.Contains("renderOrderLines();", content, StringComparison.Ordinal);
            Assert.Contains("syncTotals();", content, StringComparison.Ordinal);
        }

        [Fact]
        public void SetupCreateOrder_Js_NewOrderStartsWithPaidNowZero()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("syncTotals();");

            Assert.Equal(0m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(0m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_AddFirstItem_KeepsPaidNowZero()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1);");

            Assert.Equal(0m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(100m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_AddSecondItem_KeepsPaidNowZero()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1); addLine(50, 1);");

            Assert.Equal(0m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(150m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_ManualPartialPayment_PersistsAfterAmountChange()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: true, gstPercent: 10m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1);");
            engine.Execute("elements.paidNow.value = '40.00'; syncTotals();");
            engine.Execute("addLine(50, 1);");

            Assert.Equal(40m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(125m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_AdvanceUsedWithZeroPayment_ComputesPendingCorrectly()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 100m);

            engine.Execute("addLine(200, 1); elements.advanceUsed.value = '50.00'; elements.paidNow.value = '0.00'; syncTotals();");

            Assert.Equal(0m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(150m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_ManualFullPayment_ProducesZeroPending()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: true, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(120, 1); elements.paidNow.value = '120.00'; syncTotals();");

            Assert.Equal(120m, ReadJsDecimal(engine, "elements.paidNow.value"));
            Assert.Equal(0m, ReadJsDecimal(engine, "elements.pendingAmountInput.value"));
        }

        [Fact]
        public void SetupCreateOrder_Js_LoadStoreMasters_Success_PopulatesServiceTypesWithoutDuplicates()
        {
            var engine = CreateSetupCreateOrderStoreMastersEngine();

            engine.Execute(@"
                loadStoreMastersFromData({
                    success: true,
                    serviceTypes: [
                        { Name: 'Dry Clean', IsActive: true },
                        { Name: 'Wash', IsActive: true },
                        { Name: 'Dry Clean', IsActive: true }
                    ],
                    items: []
                });
            ");

            Assert.Equal(3m, ReadJsDecimal(engine, "elements.cfgServiceType.options.length"));
            Assert.Equal(0m, ReadJsDecimal(engine, "toastLog.length"));
            Assert.Equal("Select service type", engine.Evaluate("elements.cfgServiceType.options[0].textContent").ToString());
        }

        [Fact]
        public void SetupCreateOrder_Js_LoadStoreMasters_SuccessWithNoActiveServices_ShowsEmptyStateMessage()
        {
            var engine = CreateSetupCreateOrderStoreMastersEngine();

            engine.Execute(@"
                loadStoreMastersFromData({
                    success: true,
                    serviceTypes: [
                        { Name: 'Dry Clean', IsActive: false }
                    ],
                    items: []
                });
            ");

            Assert.Equal("No active service types configured", engine.Evaluate("elements.cfgServiceType.options[0].textContent").ToString());
            Assert.Equal("No active service types configured for this store.", engine.Evaluate("toastLog[0].message").ToString());
        }

        [Fact]
        public void SetupCreateOrder_Js_LoadStoreMasters_SessionExpired_ShowsReturnedErrorMessage()
        {
            var engine = CreateSetupCreateOrderStoreMastersEngine();

            engine.Execute(@"
                loadStoreMastersFromData({
                    success: false,
                    message: 'Session expired. Please log in again.'
                });
            ");

            Assert.Equal("No active service types configured", engine.Evaluate("elements.cfgServiceType.options[0].textContent").ToString());
            Assert.Equal("Session expired. Please log in again.", engine.Evaluate("toastLog[0].message").ToString());
        }

        [Fact]
        public void SetupCreateOrder_Js_LoadStoreMasters_RequestFailure_ShowsFailureMessage()
        {
            var engine = CreateSetupCreateOrderStoreMastersEngine();

            engine.Execute("loadStoreMastersError(new Error('Unable to load store master data.')); ");

            Assert.Equal("No active service types configured", engine.Evaluate("elements.cfgServiceType.options[0].textContent").ToString());
            Assert.Equal("Unable to load store master data.", engine.Evaluate("toastLog[0].message").ToString());
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_UsesPreloadedComponentMetadata_WhenUpsertResponseOmitsComponents()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 111, Code = "CGST", RatePercent = 9m, IsActive = true, Priority = 1, ConcurrencyToken = "cgst-token" },
                        new TaxInvoiceComponentDto { Id = 112, Code = "SGST", RatePercent = 9m, IsActive = true, Priority = 2, ConcurrencyToken = "sgst-token" }
                    }
                },
                UpsertTaxConfigurationResponse = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 8.5m,
                SgstRatePercent = 9.5m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(2, customerApi.UpdateComponentRequests.Count);
            Assert.Empty(customerApi.AddComponentRequests);
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "CGST" && x.ComponentId == 111 && x.ConcurrencyToken == "cgst-token");
            Assert.Contains(customerApi.UpdateComponentRequests, x => x.Code == "SGST" && x.ComponentId == 112 && x.ConcurrencyToken == "sgst-token");
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_GstSplitExceedsTotal_BlocksSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 31, Code = "CGST", RatePercent = 10m, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 32, Code = "SGST", RatePercent = 10m, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 10m,
                SgstRatePercent = 10m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<ViewResult>(result);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_GstSplitExceedsTotal_ReturnsViewWithWarningMessage()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 61, Code = "CGST", RatePercent = 10m, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 62, Code = "SGST", RatePercent = 10m, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 10m,
                SgstRatePercent = 10m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<TaxConfigurationViewModel>(view.Model);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);
            Assert.Equal(10m, model.CgstRatePercent);
            Assert.Equal(10m, model.SgstRatePercent);

            var userMessage = Assert.IsType<string>(controller.TempData["UserMessage"]);
            Assert.Contains("cannot exceed", userMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_WithoutGstSplitValues_WhenExistingSplitDoesNotMatchTotal_BlocksSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 51, Code = "CGST", RatePercent = 8m, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 52, Code = "SGST", RatePercent = 8m, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = null,
                SgstRatePercent = null,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<ViewResult>(result);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_GstSplitLowerThanTotal_BlocksSave()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 41, Code = "CGST", RatePercent = 9m, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 42, Code = "SGST", RatePercent = 9m, IsActive = true }
                    }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "GST",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                CgstRatePercent = 8m,
                SgstRatePercent = 8m,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<ViewResult>(result);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_DifferentSystemRequiresExplicitConfirmation()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var withoutConfirm = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "VAT",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = false
            });

            Assert.IsType<ViewResult>(withoutConfirm);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);

            var withConfirm = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "VAT",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = true,
                GstPercent = 18,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(withConfirm);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_NoTaxRequiresExplicitConfirmation()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            });
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var withoutConfirm = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "NoTax",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = false,
                GstPercent = 0,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true,
                NoTaxConfirmed = false
            });

            Assert.IsType<ViewResult>(withoutConfirm);
            Assert.Equal(0, customerApi.UpsertTaxConfigurationCount);

            var withConfirm = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = "NoTax",
                RecommendedTaxMode = "GST",
                TenantRegisteredCountryCode = "IN",
                OriginalCountryCode = "IN",
                CountryCode = "IN",
                CurrencyCode = "INR",
                EnableTax = false,
                GstPercent = 0,
                InvoicePrefix = "INV-",
                NextInvoiceNumber = 1,
                InvoiceNumberPadding = 4,
                TaxModeOverrideConfirmed = true,
                NoTaxConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(withConfirm);
            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
        }

        [Fact]
        public async Task TaxInvoiceSettings_Post_PreservesExistingSelection_WhenRecommendedDiffers()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "SalesTax",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 18,
                    InvoicePrefix = "INV-",
                    NextInvoiceNumber = 1,
                    InvoiceNumberPadding = 4
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse { TenantName = "Excel Laundry", CountryCode = "IN", CurrencyCode = "INR" }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var getResult = await controller.TaxInvoiceSettings();
            var getView = Assert.IsType<ViewResult>(getResult);
            var loaded = Assert.IsType<TaxConfigurationViewModel>(getView.Model);

            Assert.Equal("SalesTax", loaded.TaxMode);
            Assert.Equal("GST", loaded.RecommendedTaxMode);

            var postResult = await controller.TaxInvoiceSettings(new TaxConfigurationViewModel
            {
                TaxMode = loaded.TaxMode,
                RecommendedTaxMode = loaded.RecommendedTaxMode,
                TenantRegisteredCountryCode = loaded.TenantRegisteredCountryCode,
                OriginalCountryCode = loaded.OriginalCountryCode,
                CountryCode = loaded.CountryCode,
                CurrencyCode = loaded.CurrencyCode,
                EnableTax = loaded.EnableTax,
                GstPercent = loaded.GstPercent,
                InvoicePrefix = loaded.InvoicePrefix,
                NextInvoiceNumber = loaded.NextInvoiceNumber,
                InvoiceNumberPadding = loaded.InvoiceNumberPadding,
                TaxModeOverrideConfirmed = true
            });

            Assert.IsType<RedirectToActionResult>(postResult);
            Assert.Equal("SalesTax", customerApi.LastUpsertTaxConfigurationRequest.TaxMode);
        }

        [Fact]
        public async Task SaveTaxComponent_ValidationFailure_DoesNotCallApi()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SaveTaxComponent(new TaxConfigurationViewModel
            {
                ComponentEditor = new TaxComponentEditorViewModel
                {
                    Code = "",
                    DisplayName = "",
                    RatePercent = 20,
                    Priority = 1
                }
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("TaxInvoiceSettings", redirect.ActionName);
            Assert.DoesNotContain(customerApi.Calls, c => c.RequestUrl == "/AddTaxInvoiceComponent" || c.RequestUrl == "/UpdateTaxInvoiceComponent");
        }

        [Fact]
        public async Task SaveTaxComponent_AddAndUpdate_CallExpectedEndpoints()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var addResult = await controller.SaveTaxComponent(new TaxConfigurationViewModel
            {
                ComponentEditor = new TaxComponentEditorViewModel
                {
                    IsEdit = false,
                    Code = "CGST",
                    DisplayName = "Central GST",
                    RatePercent = 9,
                    Priority = 1,
                    IsCompound = false,
                    IsActive = true,
                    EffectiveFromUtcText = "2026-01-01T00:00"
                }
            });

            Assert.IsType<RedirectToActionResult>(addResult);
            Assert.Contains(customerApi.Calls, c => c.RequestUrl == "/AddTaxInvoiceComponent" && c.Method == Method.POST);

            var updateResult = await controller.SaveTaxComponent(new TaxConfigurationViewModel
            {
                ComponentEditor = new TaxComponentEditorViewModel
                {
                    IsEdit = true,
                    ComponentId = 9,
                    Code = "CGST",
                    DisplayName = "Central GST Updated",
                    RatePercent = 9.5m,
                    Priority = 1,
                    IsCompound = false,
                    IsActive = true,
                    EffectiveToUtcText = "2026-12-31T23:59",
                    ConcurrencyToken = "etag-1"
                }
            });

            Assert.IsType<RedirectToActionResult>(updateResult);
            Assert.Contains(customerApi.Calls, c => c.RequestUrl == "/UpdateTaxInvoiceComponent" && c.Method == Method.PUT);

            Assert.NotNull(customerApi.LastAddOrUpdateComponentRequest);
            Assert.Equal("tenant@example.com", customerApi.LastAddOrUpdateComponentRequest.TenantName);
            Assert.Equal("S1", customerApi.LastAddOrUpdateComponentRequest.StoreCode);
        }

        [Fact]
        public async Task GetStoreMasters_EmptyArrayResponses_AreHandledAsEmptyCollections()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoreServiceMastersRawResponse = "[]",
                StoreItemMastersRawResponse = "[]"
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.GetStoreMasters(includeInactive: false);
            var json = Assert.IsType<JsonResult>(result);
            var payload = JObject.FromObject(json.Value);

            Assert.True(payload.Value<bool>("success"));
            Assert.Empty(payload["serviceTypes"] as JArray ?? new JArray());
            Assert.Empty(payload["items"] as JArray ?? new JArray());
        }

        [Fact]
        public async Task GetStoreMasters_NonEmptyArrayResponses_AreReturned()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoreServiceMastersListResponse = new List<StoreServiceMasterDto>
                {
                    new StoreServiceMasterDto { MasterType = "Service", Name = "Dry Clean", IsActive = true }
                },
                StoreItemMastersListResponse = new List<StoreItemMasterDto>
                {
                    new StoreItemMasterDto { ServiceType = "Dry Clean", Category = "Men", ItemName = "Shirt", IsActive = true }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.GetStoreMasters(includeInactive: false);
            var json = Assert.IsType<JsonResult>(result);
            var payload = JObject.FromObject(json.Value);

            Assert.True(payload.Value<bool>("success"));
            Assert.Contains(payload["serviceTypes"] as JArray ?? new JArray(), x => string.Equals(x?["Name"]?.ToString(), "Dry Clean", StringComparison.Ordinal));
            Assert.Contains(payload["items"] as JArray ?? new JArray(), x => string.Equals(x?["ItemName"]?.ToString(), "Shirt", StringComparison.Ordinal));
        }

        [Fact]
        public async Task GetStoreMasters_ObjectResponses_AreReturned()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoreServiceMastersRawResponse = "{\"serviceTypes\":[{\"MasterType\":\"Service\",\"Name\":\"Wash\",\"IsActive\":true}]}",
                StoreItemMastersRawResponse = "{\"items\":[{\"ServiceType\":\"Wash\",\"Category\":\"Household\",\"ItemName\":\"Blanket\",\"IsActive\":true}]}"
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.GetStoreMasters(includeInactive: false);
            var json = Assert.IsType<JsonResult>(result);
            var payload = JObject.FromObject(json.Value);

            Assert.True(payload.Value<bool>("success"));
            Assert.Contains(payload["serviceTypes"] as JArray ?? new JArray(), x => string.Equals(x?["Name"]?.ToString(), "Wash", StringComparison.Ordinal));
            Assert.Contains(payload["items"] as JArray ?? new JArray(), x => string.Equals(x?["ItemName"]?.ToString(), "Blanket", StringComparison.Ordinal));
        }

        [Fact]
        public async Task GetStoreMasters_ErrorResponse_ReturnsFailurePayload()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                ThrowOnStoreServiceMastersRaw = new InvalidOperationException("Master API unavailable")
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.GetStoreMasters(includeInactive: false);
            var json = Assert.IsType<JsonResult>(result);
            var payload = JObject.FromObject(json.Value);

            Assert.False(payload.Value<bool>("success"));
            Assert.Contains("Unable to load store master data", payload["message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetStoreMasters_PassesTenantStoreAndIncludeInactive_QueryParameters()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoreServiceMastersRawResponse = "[]",
                StoreItemMastersRawResponse = "[]"
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            await controller.GetStoreMasters(includeInactive: true);

            var serviceQuery = customerApi.QueryCalls.First(x => x.RequestUrl == "/GetStoreServiceMasters");
            var itemQuery = customerApi.QueryCalls.First(x => x.RequestUrl == "/GetStoreItemMasters");

            Assert.Equal("tenant@example.com", GetQueryValue(serviceQuery.Query, "tenantName"));
            Assert.Equal("S1", GetQueryValue(serviceQuery.Query, "storeCode"));
            Assert.Equal("true", GetQueryValue(serviceQuery.Query, "includeInactive"), ignoreCase: true);

            Assert.Equal("tenant@example.com", GetQueryValue(itemQuery.Query, "tenantName"));
            Assert.Equal("S1", GetQueryValue(itemQuery.Query, "storeCode"));
            Assert.Equal("true", GetQueryValue(itemQuery.Query, "includeInactive"), ignoreCase: true);
        }

        [Fact]
        public async Task DeactivateTaxComponent_CallsDeactivateEndpoint()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.DeactivateTaxComponent(42, "etag-42");

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Contains(customerApi.Calls, c => c.RequestUrl == "/DeactivateTaxInvoiceComponent" && c.Method == Method.POST);
            Assert.NotNull(customerApi.LastDeactivateRequest);
            Assert.Equal(42, customerApi.LastDeactivateRequest.ComponentId);
            Assert.Equal("etag-42", customerApi.LastDeactivateRequest.ConcurrencyToken);
        }

        private static CustomerController CreateController(InMemoryCustomerApiClient customerApi, InMemoryTaxLoginApiClient loginApi)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var storeCfg = new FakeStoreConfigService(new StoreConfigurationViewModel(), new StoreConfigurationViewModel());
            var currencyService = new CurrencyService(storeCfg, cache);
            var config = new ConfigurationBuilder().Build();
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
            var dbContext = new ApplicationDbContext(dbOptions);

            return new CustomerController(
                cache,
                customerApi,
                loginApi,
                storeCfg,
                currencyService,
                config,
                dbContext,
                NullLogger<CustomerController>.Instance);
        }

        private static void SetControllerSession(Controller controller, string tenantName, string storeCode, string userRole)
        {
            var httpContext = new DefaultHttpContext();
            var session = new TestSession();
            session.Set("TenantName", System.Text.Encoding.UTF8.GetBytes(tenantName));
            session.Set("TenantStore", System.Text.Encoding.UTF8.GetBytes(storeCode));
            session.Set("UserRole", System.Text.Encoding.UTF8.GetBytes(userRole));
            httpContext.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        }

        private static async Task<T> InvokePrivateAsync<T>(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = method.Invoke(target, args) as Task<T>;
            Assert.NotNull(task);
            return await task;
        }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static T InvokePrivateStaticWithOut<T>(Type type, string methodName, object input, out string output)
        {
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var args = new object[] { input, null };
            var result = (T)method.Invoke(null, args);
            output = args[1] as string;
            return result;
        }

        private static Engine CreateSetupCreateOrderCalculationEngine(bool allowPartialPayment, bool taxEnabled, decimal gstPercent, bool pricesIncludeTax, decimal currentAdvanceBalance)
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);
            var parseAmountFunction = ExtractFunctionBlock(content, "parseAmount");
            var computeOrderAmountFunction = ExtractFunctionBlock(content, "computeOrderAmount");
            var computeTaxBreakupFunction = ExtractFunctionBlock(content, "computeTaxBreakup");
            var syncTotalsFunction = ExtractFunctionBlock(content, "syncTotals");

            var script = $@"
                var setupOnly = false;
                var currentOrderMode = 'pieces';
                var selectedService = 'Dry Clean';
                var orderLines = [];
                var taxEnabled = {(taxEnabled ? "true" : "false")};
                var gstPercent = {gstPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                var pricesIncludeTax = {(pricesIncludeTax ? "true" : "false")};
                var allowPartialPayment = {(allowPartialPayment ? "true" : "false")};
                var currentAdvanceBalance = {currentAdvanceBalance.ToString(System.Globalization.CultureInfo.InvariantCulture)};

                function makeElement(initialValue) {{
                    return {{
                        value: initialValue || '',
                        textContent: '',
                        style: {{}},
                        classList: {{ toggle: function () {{ }} }},
                        disabled: false
                    }};
                }}

                var elements = {{
                    advanceUsed: makeElement('0.00'),
                    paidNow: makeElement('0.00'),
                    orderAmountDisplay: makeElement(''),
                    netPayableDisplay: makeElement(''),
                    paidNowDisplay: makeElement(''),
                    pendingAmountDisplay: makeElement(''),
                    advUsedDisplay: makeElement(''),
                    advRemainingDisplay: makeElement(''),
                    orderAmount: makeElement('0.00'),
                    netPayableInput: makeElement('0.00'),
                    pendingAmountInput: makeElement('0.00'),
                    orderMode: makeElement('pieces'),
                    subTotalDisplay: makeElement(''),
                    cgstDisplay: makeElement(''),
                    sgstDisplay: makeElement(''),
                    totalAmountDisplay: makeElement(''),
                    serviceType: makeElement('Dry Clean'),
                    weightAmountDisplay: makeElement('0.00'),
                    weightInKg: makeElement('0'),
                    ratePerKg: makeElement('0'),
                    weightServiceType: makeElement('Wash & Fold')
                }};

                var document = {{
                    getElementById: function (id) {{
                        if (!elements[id]) {{
                            elements[id] = makeElement('');
                        }}
                        return elements[id];
                    }}
                }};

                function formatCurrency(value) {{
                    var n = Number(value);
                    return isNaN(n) ? '0.00' : n.toFixed(2);
                }}

                {parseAmountFunction}
                {computeOrderAmountFunction}
                {computeTaxBreakupFunction}
                {syncTotalsFunction}

                function addLine(price, qty) {{
                    orderLines.push({{ service: selectedService, category: 'General', name: 'Item ' + orderLines.length, price: Number(price || 0), qty: Number(qty || 0) }});
                    syncTotals();
                }}
            ";

            var engine = new Engine();
            engine.Execute(script);
            return engine;
        }

        private static Engine CreateSetupCreateOrderStoreMastersEngine()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "SetupCreateOrder.cshtml"));

            var content = File.ReadAllText(viewPath);
            var fillMasterSelectFunction = ExtractFunctionBlock(content, "fillMasterSelect");

            var applyStartToken = "function applyStoreMastersResponse(data)";
            var applyStart = content.IndexOf(applyStartToken, StringComparison.Ordinal);
            Assert.True(applyStart >= 0, "Unable to locate applyStoreMastersResponse in SetupCreateOrder.cshtml.");
            var applyFunction = ExtractFunctionBlock(content.Substring(applyStart), "applyStoreMastersResponse");

            var failStartToken = "function handleStoreMastersLoadFailure(error)";
            var failStart = content.IndexOf(failStartToken, StringComparison.Ordinal);
            Assert.True(failStart >= 0, "Unable to locate handleStoreMastersLoadFailure in SetupCreateOrder.cshtml.");
            var failFunction = ExtractFunctionBlock(content.Substring(failStart), "handleStoreMastersLoadFailure");

            var script = $@"
                var masterItems = [];
                var toastLog = [];
                var refreshCalls = 0;
                var updateCalls = 0;

                function makeSelect() {{
                    return {{
                        value: '',
                        options: [],
                        appendChild: function (option) {{ this.options.push(option); }},
                        set innerHTML(v) {{ this.options = []; }},
                        get innerHTML() {{ return ''; }}
                    }};
                }}

                var elements = {{
                    cfgServiceType: makeSelect(),
                    cfgCategory: makeSelect()
                }};

                var document = {{
                    getElementById: function (id) {{ return elements[id] || null; }},
                    createElement: function () {{ return {{ value: '', textContent: '' }}; }}
                }};

                function showToast(message, isSuccess) {{ toastLog.push({{ message: message, isSuccess: !!isSuccess }}); }}
                function refreshCategoryOptionsForSelectedService() {{ refreshCalls++; }}
                function updateConfigItemSuggestions() {{ updateCalls++; }}

                {fillMasterSelectFunction}
                {applyFunction}
                {failFunction}

                function loadStoreMastersFromData(data) {{ applyStoreMastersResponse(data); }}
                function loadStoreMastersError(error) {{ handleStoreMastersLoadFailure(error); }}
            ";

            var engine = new Engine();
            engine.Execute(script);
            return engine;
        }

        private static string ExtractFunctionBlock(string content, string functionName)
        {
            var signature = $"function {functionName}(";
            var start = content.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Unable to find function '{functionName}' in SetupCreateOrder.cshtml.");

            var openingBrace = content.IndexOf('{', start);
            Assert.True(openingBrace >= 0, $"Unable to find opening brace for function '{functionName}'.");

            var depth = 0;
            for (var i = openingBrace; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    depth++;
                }
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return content.Substring(start, i - start + 1);
                    }
                }
            }

            throw new InvalidOperationException($"Unable to extract complete function block for '{functionName}'.");
        }

        private static decimal ReadJsDecimal(Engine engine, string expression)
        {
            var value = engine.Evaluate($"Number({expression})").ToObject();
            return Convert.ToDecimal(value);
        }

        private static string GetQueryValue(Dictionary<string, string> query, string key)
        {
            var pair = query.First(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase));
            return pair.Value;
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public ISession Session { get; set; } = null!;
        }

        private sealed class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private sealed class InMemoryCustomerApiClient : ICustomerApiClient
        {
            public Queue<TaxConfigurationDto> GetTaxConfigurationResponses { get; } = new Queue<TaxConfigurationDto>();
            public TaxConfigurationDto StoredTaxConfiguration { get; set; }
            public TaxModeResolutionDto ResolveTaxModeResponse { get; set; } = new TaxModeResolutionDto { TaxMode = "GST" };
            public TaxConfigurationDto UpsertTaxConfigurationResponse { get; set; }
            public bool ForceInvalidComponentSaveResponse { get; set; }
            public List<StoreServiceMasterDto> StoreServiceMastersListResponse { get; set; } = new List<StoreServiceMasterDto>();
            public string StoreServiceMastersRawResponse { get; set; } = string.Empty;
            public List<StoreItemMasterDto> StoreItemMastersListResponse { get; set; } = new List<StoreItemMasterDto>();
            public string StoreItemMastersRawResponse { get; set; } = string.Empty;
            public List<LaundryItemPriceDto> LaundryItemPricesResponse { get; set; } = new List<LaundryItemPriceDto>();
            public Exception ThrowOnStoreServiceMastersRaw { get; set; }
            public int UpsertTaxConfigurationCount { get; private set; }
            public TaxConfigurationUpsertRequestDto LastUpsertTaxConfigurationRequest { get; private set; }
            public TaxInvoiceComponentUpsertRequestDto LastAddOrUpdateComponentRequest { get; private set; }
            public TaxInvoiceComponentDeactivateRequestDto LastDeactivateRequest { get; private set; }
            public List<TaxInvoiceComponentUpsertRequestDto> AddComponentRequests { get; } = new List<TaxInvoiceComponentUpsertRequestDto>();
            public List<TaxInvoiceComponentUpsertRequestDto> UpdateComponentRequests { get; } = new List<TaxInvoiceComponentUpsertRequestDto>();
            public List<(string RequestUrl, Method Method)> Calls { get; } = new List<(string RequestUrl, Method Method)>();
            public List<(string RequestUrl, Method Method, Dictionary<string, string> Query)> QueryCalls { get; } = new List<(string RequestUrl, Method Method, Dictionary<string, string> Query)>();
            public Exception ThrowOnStoreItemMastersRaw { get; set; }

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));

                if (string.Equals(requestUrl, "/UpsertTaxConfiguration", StringComparison.OrdinalIgnoreCase))
                {
                    UpsertTaxConfigurationCount++;
                    LastUpsertTaxConfigurationRequest = ToObject<TaxConfigurationUpsertRequestDto>(model);
                    var existingComponents = StoredTaxConfiguration?.Components != null
                        ? StoredTaxConfiguration.Components.Select(c => new TaxInvoiceComponentDto
                        {
                            Id = c.Id,
                            Code = c.Code,
                            DisplayName = c.DisplayName,
                            RatePercent = c.RatePercent,
                            Jurisdiction = c.Jurisdiction,
                            Priority = c.Priority,
                            IsCompound = c.IsCompound,
                            IsActive = c.IsActive,
                            EffectiveFromUtc = c.EffectiveFromUtc,
                            EffectiveToUtc = c.EffectiveToUtc,
                            ConcurrencyToken = c.ConcurrencyToken
                        }).ToList()
                        : new List<TaxInvoiceComponentDto>();

                    StoredTaxConfiguration = new TaxConfigurationDto
                    {
                        TenantName = LastUpsertTaxConfigurationRequest.TenantName,
                        StoreCode = LastUpsertTaxConfigurationRequest.StoreCode,
                        TaxMode = LastUpsertTaxConfigurationRequest.TaxMode,
                        CountryCode = LastUpsertTaxConfigurationRequest.CountryCode,
                        CurrencyCode = LastUpsertTaxConfigurationRequest.CurrencyCode,
                        IsPricesInclusive = LastUpsertTaxConfigurationRequest.IsPricesInclusive,
                        EnableTax = LastUpsertTaxConfigurationRequest.EnableTax ?? false,
                        GstPercent = LastUpsertTaxConfigurationRequest.GstPercent ?? 0,
                        PricesIncludeTax = LastUpsertTaxConfigurationRequest.PricesIncludeTax ?? false,
                        InvoicePrefix = LastUpsertTaxConfigurationRequest.InvoicePrefix,
                        NextInvoiceNumber = LastUpsertTaxConfigurationRequest.NextInvoiceNumber ?? 1,
                        InvoiceNumberPadding = LastUpsertTaxConfigurationRequest.InvoiceNumberPadding ?? 4,
                        ResetInvoiceNumberYearly = LastUpsertTaxConfigurationRequest.ResetInvoiceNumberYearly ?? false,
                        Notes = LastUpsertTaxConfigurationRequest.Notes,
                        Components = existingComponents
                    };

                    return Task.FromResult(ToGeneric<T>(UpsertTaxConfigurationResponse ?? StoredTaxConfiguration));
                }

                if (string.Equals(requestUrl, "/AddTaxInvoiceComponent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requestUrl, "/UpdateTaxInvoiceComponent", StringComparison.OrdinalIgnoreCase))
                {
                    LastAddOrUpdateComponentRequest = ToObject<TaxInvoiceComponentUpsertRequestDto>(model);

                    if (string.Equals(requestUrl, "/AddTaxInvoiceComponent", StringComparison.OrdinalIgnoreCase))
                    {
                        AddComponentRequests.Add(LastAddOrUpdateComponentRequest);
                        if (StoredTaxConfiguration != null)
                        {
                            StoredTaxConfiguration.Components ??= new List<TaxInvoiceComponentDto>();
                            StoredTaxConfiguration.Components.Add(new TaxInvoiceComponentDto
                            {
                                Id = LastAddOrUpdateComponentRequest.ComponentId ?? (100 + AddComponentRequests.Count),
                                Code = LastAddOrUpdateComponentRequest.Code,
                                DisplayName = LastAddOrUpdateComponentRequest.DisplayName,
                                RatePercent = LastAddOrUpdateComponentRequest.RatePercent,
                                Jurisdiction = LastAddOrUpdateComponentRequest.Jurisdiction,
                                Priority = LastAddOrUpdateComponentRequest.Priority,
                                IsCompound = LastAddOrUpdateComponentRequest.IsCompound,
                                IsActive = LastAddOrUpdateComponentRequest.IsActive,
                                EffectiveFromUtc = LastAddOrUpdateComponentRequest.EffectiveFromUtc,
                                EffectiveToUtc = LastAddOrUpdateComponentRequest.EffectiveToUtc,
                                ConcurrencyToken = LastAddOrUpdateComponentRequest.ConcurrencyToken
                            });
                        }
                    }
                    else
                    {
                        UpdateComponentRequests.Add(LastAddOrUpdateComponentRequest);
                    }

                    var response = new TaxInvoiceComponentDto
                    {
                        Id = LastAddOrUpdateComponentRequest.ComponentId ?? 101,
                        Code = LastAddOrUpdateComponentRequest.Code,
                        DisplayName = LastAddOrUpdateComponentRequest.DisplayName,
                        RatePercent = LastAddOrUpdateComponentRequest.RatePercent,
                        Priority = LastAddOrUpdateComponentRequest.Priority,
                        IsCompound = LastAddOrUpdateComponentRequest.IsCompound,
                        IsActive = LastAddOrUpdateComponentRequest.IsActive,
                        ConcurrencyToken = LastAddOrUpdateComponentRequest.ConcurrencyToken
                    };

                    if (ForceInvalidComponentSaveResponse)
                    {
                        response.Code = null;
                    }

                    return Task.FromResult(ToGeneric<T>(response));
                }

                if (string.Equals(requestUrl, "/DeactivateTaxInvoiceComponent", StringComparison.OrdinalIgnoreCase))
                {
                    LastDeactivateRequest = ToObject<TaxInvoiceComponentDeactivateRequestDto>(model);
                    var response = new TaxInvoiceComponentDto { Id = LastDeactivateRequest.ComponentId, IsActive = false };
                    return Task.FromResult(ToGeneric<T>(response));
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                QueryCalls.Add((requestUrl, method, queryParams != null
                    ? queryParams.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

                if (string.Equals(requestUrl, "/GetTaxConfiguration", StringComparison.OrdinalIgnoreCase))
                {
                    if (GetTaxConfigurationResponses.Count > 0)
                    {
                        var next = GetTaxConfigurationResponses.Dequeue();
                        return Task.FromResult(ToGeneric<T>(next));
                    }

                    return Task.FromResult(ToGeneric<T>(StoredTaxConfiguration));
                }

                if (string.Equals(requestUrl, "/ResolveTaxMode", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(ResolveTaxModeResponse));
                }

                if (string.Equals(requestUrl, "/GetStoreServiceMasters", StringComparison.OrdinalIgnoreCase))
                {
                    if (typeof(T) == typeof(List<StoreServiceMasterDto>))
                    {
                        return Task.FromResult(ToGeneric<T>(StoreServiceMastersListResponse));
                    }

                    if (typeof(T) == typeof(JObject))
                    {
                        if (ThrowOnStoreServiceMastersRaw != null)
                        {
                            throw ThrowOnStoreServiceMastersRaw;
                        }

                        var raw = StoreServiceMastersRawResponse ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            return Task.FromResult(default(T));
                        }

                        var token = JToken.Parse(raw);
                        if (token is JObject serviceObject)
                        {
                            return Task.FromResult((T)(object)serviceObject);
                        }

                        if (token is JArray serviceArray)
                        {
                            throw new ApiUnavailableException(
                                "Master API JSON diagnostic: HTTP 200; ContentType=application/json; ContentLength=2; BodyState=InvalidJsonObject.",
                                null,
                                200,
                                serviceArray.ToString(Formatting.None));
                        }

                        return Task.FromResult(default(T));
                    }
                }

                if (string.Equals(requestUrl, "/GetStoreItemMasters", StringComparison.OrdinalIgnoreCase))
                {
                    if (typeof(T) == typeof(List<StoreItemMasterDto>))
                    {
                        return Task.FromResult(ToGeneric<T>(StoreItemMastersListResponse));
                    }

                    if (typeof(T) == typeof(JObject))
                    {
                        if (ThrowOnStoreItemMastersRaw != null)
                        {
                            throw ThrowOnStoreItemMastersRaw;
                        }

                        var raw = StoreItemMastersRawResponse ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            return Task.FromResult(default(T));
                        }

                        var token = JToken.Parse(raw);
                        if (token is JObject itemObject)
                        {
                            return Task.FromResult((T)(object)itemObject);
                        }

                        if (token is JArray itemArray)
                        {
                            throw new ApiUnavailableException(
                                "Master API JSON diagnostic: HTTP 200; ContentType=application/json; ContentLength=2; BodyState=InvalidJsonObject.",
                                null,
                                200,
                                itemArray.ToString(Formatting.None));
                        }

                        return Task.FromResult(default(T));
                    }
                }

                if (string.Equals(requestUrl, "/GetLaundryItemPrices", StringComparison.OrdinalIgnoreCase))
                {
                    if (typeof(T) == typeof(List<LaundryItemPriceDto>))
                    {
                        return Task.FromResult(ToGeneric<T>(LaundryItemPricesResponse));
                    }
                }

                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            public Task<T> SendRequestForZippedAsync<T>(string requestUrl, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, List<KeyValuePair<string, string>> queryParams, Method method) where T : new()
            {
                Calls.Add((requestUrl, method));
                return Task.FromResult(new T());
            }

            private static T ToGeneric<T>(object model) where T : new()
            {
                if (model == null)
                {
                    return default;
                }

                if (model is T typed)
                {
                    return typed;
                }

                var json = JsonConvert.SerializeObject(model);
                return JsonConvert.DeserializeObject<T>(json);
            }

            private static TModel ToObject<TModel>(object model)
            {
                if (model is TModel typed)
                {
                    return typed;
                }

                var json = JsonConvert.SerializeObject(model);
                return JsonConvert.DeserializeObject<TModel>(json);
            }
        }

        private sealed class InMemoryTaxLoginApiClient : ILoginApiClient
        {
            public LoginIoResponse ProfileResponse { get; set; } = new LoginIoResponse();

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (string.Equals(requestUrl, "/TenantprofileDetails", StringComparison.OrdinalIgnoreCase))
                {
                    if (ProfileResponse is T typed)
                    {
                        return Task.FromResult(typed);
                    }

                    var json = JsonConvert.SerializeObject(ProfileResponse);
                    var value = JsonConvert.DeserializeObject<T>(json);
                    return Task.FromResult(value);
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
