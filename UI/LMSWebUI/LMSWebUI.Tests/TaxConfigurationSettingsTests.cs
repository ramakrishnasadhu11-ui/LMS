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
            Assert.False(request.EnableTax);
            Assert.Equal(0m, request.GstPercent);
            Assert.Equal("NORTH-STORE9-", request.InvoicePrefix);
        }

        [Fact]
        public async Task BuildDefaultTaxConfigurationRequest_UnknownCountry_DoesNotForceIndiaGstDefaults()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Global Laundry",
                    CountryCode = "BR",
                    CurrencyCode = null
                }
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S9", "Admin");

            var request = await InvokePrivateAsync<TaxConfigurationUpsertRequestDto>(
                controller,
                "BuildDefaultTaxConfigurationRequestAsync",
                "tenant@example.com",
                "S9",
                "Global Laundry");

            Assert.Equal("NoTax", request.TaxMode);
            Assert.Equal("BR", request.CountryCode);
            Assert.Equal("USD", request.CurrencyCode);
            Assert.False(request.EnableTax);
            Assert.Equal(0m, request.GstPercent);
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
            Assert.Contains("config.EnableTax && totalRate > 0m", content, StringComparison.Ordinal);
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
        public void SetupCreateOrder_Js_PartialDisabled_Unpaid_IsAllowed()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: false, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1);");

            Assert.False(engine.Evaluate("!allowPartialPayment && parseAmount(elements.paidNow.value) > 0 && parseAmount(elements.paidNow.value) < parseAmount(elements.netPayableInput.value)").AsBoolean());
        }

        [Fact]
        public void SetupCreateOrder_Js_PartialDisabled_FullPayment_IsAllowed()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: false, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1); elements.paidNow.value = '100.00'; syncTotals();");

            Assert.False(engine.Evaluate("!allowPartialPayment && parseAmount(elements.paidNow.value) > 0 && parseAmount(elements.paidNow.value) < parseAmount(elements.netPayableInput.value)").AsBoolean());
        }

        [Fact]
        public void SetupCreateOrder_Js_PartialDisabled_GenuinePartial_IsRejected()
        {
            var engine = CreateSetupCreateOrderCalculationEngine(allowPartialPayment: false, taxEnabled: false, gstPercent: 0m, pricesIncludeTax: false, currentAdvanceBalance: 0m);

            engine.Execute("addLine(100, 1); elements.paidNow.value = '40.00'; syncTotals();");

            Assert.True(engine.Evaluate("!allowPartialPayment && parseAmount(elements.paidNow.value) > 0 && parseAmount(elements.paidNow.value) < parseAmount(elements.netPayableInput.value)").AsBoolean());
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
        public async Task SetupCreateOrder_Get_InactiveStoreScope_RedirectsToDashboard()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': false }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder();

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Dashboard", redirect.ControllerName);

            var userMessage = Assert.IsType<string>(controller.TempData["UserMessage"]);
            Assert.Contains("inactive", userMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetupCreateOrder_Get_UsesModernTaxConfiguration_ForUkVatStore()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "rksadhu11@gmail.com",
                    StoreCode = "RTJ",
                    TaxMode = "VAT",
                    CountryCode = "GB",
                    CurrencyCode = "GBP",
                    EnableTax = true,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 11, Code = "VAT", DisplayName = "VAT", RatePercent = 20m, Priority = 1, IsActive = true }
                    }
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['RTJ'],
                    'StoreStatuses': [
                        { 'StoreCode': 'RTJ', 'TenantName': 'rksadhu11@gmail.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "rksadhu11@gmail.com", "RTJ", "Admin");

            var result = await controller.SetupCreateOrder();

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("VAT", Convert.ToString(view.ViewData["TaxMode"]));
            Assert.True(Convert.ToBoolean(view.ViewData["TaxEnabled"]));
            Assert.Equal(20m, Convert.ToDecimal(view.ViewData["GstPercent"]));
            Assert.Contains("VAT", Convert.ToString(view.ViewData["TaxComponentsJson"]), StringComparison.OrdinalIgnoreCase);

            var taxQuery = customerApi.QueryCalls.FirstOrDefault(x => string.Equals(x.RequestUrl, "/GetTaxConfiguration", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("rksadhu11@gmail.com", GetQueryValue(taxQuery.Query, "tenantName"));
            Assert.Equal("RTJ", GetQueryValue(taxQuery.Query, "storeCode"));
        }

        [Fact]
        public async Task SetupCreateOrder_Get_MissingPaymentSettings_AutoCreatesSafeDefault_ForTenantStore()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder();

            var view = Assert.IsType<ViewResult>(result);
            var enabledModes = Assert.IsType<List<string>>(view.ViewData["EnabledPaymentModes"]);
            Assert.Equal(new[] { "Cash" }, enabledModes);

            Assert.Equal(1, customerApi.SavePaymentSettingsCount);
            Assert.NotNull(customerApi.LastSavedPaymentSettings);
            Assert.Equal("tenant@example.com", customerApi.LastSavedPaymentSettings.TenantName);
            Assert.Equal("S1", customerApi.LastSavedPaymentSettings.StoreCode);
            Assert.True(customerApi.LastSavedPaymentSettings.EnableCash);
            Assert.False(customerApi.LastSavedPaymentSettings.EnableUpi);
            Assert.False(customerApi.LastSavedPaymentSettings.EnableCard);
            Assert.False(customerApi.LastSavedPaymentSettings.EnableWallet);
            Assert.Equal("Cash", customerApi.LastSavedPaymentSettings.DefaultPaymentMode);
        }

        [Fact]
        public async Task SetupCreateOrder_Get_ExistingPaymentSettings_DoesNotOverwrite()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = true,
                    EnableUpi = true,
                    EnableCard = false,
                    EnableWallet = false,
                    DefaultPaymentMode = "UPI",
                    AllowPartialPayment = true
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder();

            var view = Assert.IsType<ViewResult>(result);
            var enabledModes = Assert.IsType<List<string>>(view.ViewData["EnabledPaymentModes"]);
            Assert.Equal(new[] { "Cash", "UPI" }, enabledModes);
            Assert.Equal(0, customerApi.SavePaymentSettingsCount);
        }

        [Fact]
        public async Task SetupCreateOrder_Get_InitializesWalkInCustomer_ForNewTenantStore()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            customerApi.SearchCustomersByText["walk-in customer"] = new List<string> { "Walk-in Customer" };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder();

            Assert.IsType<ViewResult>(result);
            Assert.Equal(1, customerApi.InsertCustomerCount);
            Assert.NotNull(customerApi.LastInsertedCustomerRequest);
            Assert.Equal("tenant@example.com", customerApi.LastInsertedCustomerRequest["TenantName"]?.ToString());
            Assert.Equal("S1", customerApi.LastInsertedCustomerRequest["StoreCode"]?.ToString());
            Assert.Equal("Walk-in Customer", customerApi.LastInsertedCustomerRequest["CustomerName"]?.ToString());
            Assert.Contains(customerApi.StoredCustomerNames, x => string.Equals(x, "Walk-in Customer", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Get_WalkInInitialization_IsIdempotentAcrossRetriesInSameSession()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                }
            };

            customerApi.SearchCustomersByText["walk-in customer"] = new List<string> { "Walk-in Customer" };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var first = await controller.SetupCreateOrder();
            var second = await controller.SetupCreateOrder();

            Assert.IsType<ViewResult>(first);
            Assert.IsType<ViewResult>(second);
            Assert.Equal(1, customerApi.InsertCustomerCount);
        }

        [Fact]
        public async Task SetupCreateOrder_Get_ExistingWalkInCustomer_DoesNotCreateDuplicate()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                CustomersByName =
                {
                    ["walk-in customer"] = new CustomerIOResponse
                    {
                        StatusCode = "200",
                        CustCode = "CUS-WALKIN",
                        CustomerName = "Walk-in Customer",
                        TenantName = "tenant@example.com"
                    }
                }
            };

            customerApi.StoredCustomerNames.Add("Walk-in Customer");

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder();

            Assert.IsType<ViewResult>(result);
            Assert.Equal(0, customerApi.InsertCustomerCount);
            Assert.Single(customerApi.StoredCustomerNames.Where(x => string.Equals(x, "Walk-in Customer", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task SearchCustomers_WalkInCustomer_AppearsInOrderSelectorSuggestions()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                SearchCustomersByText =
                {
                    ["walk"] = new List<string> { "Walk-in Customer" }
                }
            };

            var controller = CreateController(customerApi, new InMemoryTaxLoginApiClient());
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SearchCustomers("walk");

            var json = Assert.IsType<JsonResult>(result);
            var names = Assert.IsAssignableFrom<IEnumerable<string>>(json.Value);
            Assert.Contains("Walk-in Customer", names);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_ComputesVatPayable_FromModernTaxConfiguration()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "UK1",
                    TaxMode = "VAT",
                    CountryCode = "GB",
                    CurrencyCode = "GBP",
                    EnableTax = true,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 21, Code = "VAT", DisplayName = "VAT", RatePercent = 20m, Priority = 1, IsActive = true }
                    }
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['UK1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'UK1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "UK1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "C001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LaundryOrderDto>(view.Model);

            Assert.Equal(100m, model.SubTotal);
            Assert.Equal(20m, model.TaxAmount);
            Assert.Equal(120m, model.TotalAmount);
            Assert.Equal(20m, model.TaxPercent);
            Assert.Equal(0m, model.CgstAmount);
            Assert.Equal(0m, model.SgstAmount);
            Assert.Equal(120m, model.PendingAmount);
            Assert.Equal(120m, model.NetPayable);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_MissingTaxConfiguration_RedirectsToTaxInvoiceSettings_AndAutoCreatesDefault()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "US1",
                    EnableCash = true,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = true
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{ 'OrderNo': 'US1-1001' }")
            };

            customerApi.GetTaxConfigurationResponses.Enqueue(null);
            customerApi.GetTaxConfigurationResponses.Enqueue(null);

            var loginApi = new InMemoryTaxLoginApiClient
            {
                ProfileResponse = new LoginIoResponse
                {
                    TenantName = "Northwind Laundry",
                    CountryCode = "US",
                    CurrencyCode = "USD"
                },
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['US1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'US1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "US1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("TaxInvoiceSettings", redirect.ActionName);

            Assert.Equal(1, customerApi.UpsertTaxConfigurationCount);
            Assert.NotNull(customerApi.LastUpsertTaxConfigurationRequest);
            Assert.Equal("US", customerApi.LastUpsertTaxConfigurationRequest.CountryCode);
            Assert.Equal("SalesTax", customerApi.LastUpsertTaxConfigurationRequest.TaxMode);
            Assert.False(customerApi.LastUpsertTaxConfigurationRequest.EnableTax);
            Assert.Equal(0m, customerApi.LastUpsertTaxConfigurationRequest.GstPercent);

            Assert.DoesNotContain(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
            var toast = Assert.IsType<string>(controller.TempData["SetupCreateOrderToast"]);
            Assert.Contains("Tax configuration is missing", toast, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_NoPaymentModeConfigured_AllowsUnpaidOrderCreation()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = false,
                    EnableUpi = false,
                    EnableCard = false,
                    EnableWallet = false,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = true
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{ 'OrderNo': 'S1-1001', 'CustCode': 'CUS-001' }")
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.Contains(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Post_NoPaymentModeConfigured_RejectsPaidNow()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = false,
                    EnableUpi = false,
                    EnableCard = false,
                    EnableWallet = false,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = true
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 10m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.DoesNotContain(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));

            var toast = Assert.IsType<string>(controller.TempData["SetupCreateOrderToast"]);
            Assert.Contains("No payment mode is configured", toast, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_PartialDisabled_UnpaidOrder_IsAllowed()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = true,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = false
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{ 'OrderNo': 'S1-1002', 'CustCode': 'CUS-001' }")
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.Contains(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Post_PartialDisabled_FullPayment_IsAllowed()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = true,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = false
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{ 'OrderNo': 'S1-1003', 'CustCode': 'CUS-001' }")
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 100m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.Contains(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Post_PartialDisabled_PartialCollected_IsRejected()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = true,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = false
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 40m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.DoesNotContain(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));

            var toast = Assert.IsType<string>(controller.TempData["SetupCreateOrderToast"]);
            Assert.Contains("Partial payments are disabled", toast, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_PartialEnabled_PartialCollected_IsAllowed()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    TaxMode = "NoTax",
                    CountryCode = "US",
                    CurrencyCode = "USD",
                    EnableTax = false,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>()
                },
                PaymentSettingsResponse = new PaymentSettingsViewModel
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "S1",
                    EnableCash = true,
                    DefaultPaymentMode = "Cash",
                    AllowPartialPayment = true
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{ 'OrderNo': 'S1-1004', 'CustCode': 'CUS-001' }")
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CUS-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 40m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.Contains(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Post_ComputesIndiaGstSplit_FromModernTaxConfiguration()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "tenant@example.com",
                    StoreCode = "IN1",
                    TaxMode = "GST",
                    CountryCode = "IN",
                    CurrencyCode = "INR",
                    EnableTax = true,
                    GstPercent = 0m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 31, Code = "CGST", DisplayName = "Central GST", RatePercent = 9m, Priority = 1, IsActive = true },
                        new TaxInvoiceComponentDto { Id = 32, Code = "SGST", DisplayName = "State GST", RatePercent = 9m, Priority = 2, IsActive = true }
                    }
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['IN1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'IN1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "IN1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "C002",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LaundryOrderDto>(view.Model);

            Assert.Equal(100m, model.SubTotal);
            Assert.Equal(18m, model.TaxAmount);
            Assert.Equal(118m, model.TotalAmount);
            Assert.Equal(18m, model.TaxPercent);
            Assert.Equal(9m, model.CgstAmount);
            Assert.Equal(9m, model.SgstAmount);
        }

        [Fact]
        public async Task SetupCreateOrder_Post_IneligibleSubscription_RedirectsToDashboard_AndSkipsCreateOrder()
        {
            var customerApi = new InMemoryCustomerApiClient();
            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['S1'],
                    'StoreStatuses': [
                        { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                    ]
                }"),
                TenantSubscriptionOverviewResponse = JObject.Parse(@"{ 'SubscriptionStatus': 'Expired' }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "tenant@example.com", "S1", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 100m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("Dashboard", redirect.ControllerName);

            var userMessage = Assert.IsType<string>(controller.TempData["UserMessage"]);
            Assert.Contains("not active for creating orders", userMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(customerApi.Calls, c => string.Equals(c.RequestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SetupCreateOrder_Post_UsesSavedCustCode_ForRedirectAndSession_WhenApiReturnsDifferentCode()
        {
            var customerApi = new InMemoryCustomerApiClient
            {
                StoredTaxConfiguration = new TaxConfigurationDto
                {
                    TenantName = "rksadhu11@gmail.com",
                    StoreCode = "RTJ",
                    TaxMode = "VAT",
                    CountryCode = "GB",
                    CurrencyCode = "GBP",
                    EnableTax = true,
                    GstPercent = 18m,
                    PricesIncludeTax = false,
                    Components = new List<TaxInvoiceComponentDto>
                    {
                        new TaxInvoiceComponentDto { Id = 51, Code = "VAT", DisplayName = "VAT", RatePercent = 18m, Priority = 1, IsActive = true }
                    }
                },
                CreateLaundryOrderResponse = JObject.Parse(@"{
                    'OrderNo':'RTJ-1001',
                    'CustCode':'CU-SAVED-001',
                    'Message':'Created'
                }")
            };

            var loginApi = new InMemoryTaxLoginApiClient
            {
                TenantStoreDetailsResponse = JObject.Parse(@"{
                    'Storecodes': ['RTJ'],
                    'StoreStatuses': [
                        { 'StoreCode': 'RTJ', 'TenantName': 'rksadhu11@gmail.com', 'IsActive': true }
                    ]
                }")
            };

            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "rksadhu11@gmail.com", "RTJ", "Admin");

            var result = await controller.SetupCreateOrder(new LaundryOrderDto
            {
                CustomerName = "Walk-in Customer",
                CustCode = "CU-REQUEST-001",
                OrderAmount = 100m,
                OrderMode = "pieces",
                PaymentMode = "Cash",
                PaidNow = 0m,
                AdvanceUsed = 0m
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("SetupCreateOrder", redirect.ActionName);
            Assert.Equal("CU-SAVED-001", redirect.RouteValues?["customerCode"]?.ToString());
            Assert.Equal("CU-SAVED-001", controller.HttpContext.Session.GetString("SelectedCustomerCode"));
        }

        [Fact]
        public async Task GetCustomerOrders_UsesTenantStoreAndResolvedCustCode_ForPostCreateRefresh()
        {
            var expectedOrder = new LaundryOrderDto
            {
                OrderNo = "RTJ-1001",
                CustCode = "CU-SAVED-001",
                CustomerName = "Walk-in Customer",
                Status = "Received",
                StoreCode = "RTJ",
                TenantName = "rksadhu11@gmail.com"
            };

            var customerApi = new InMemoryCustomerApiClient
            {
                LaundryOrdersResponse = new LaundryOrderResponseDto
                {
                    StatusCode = "200",
                    CustCode = "CU-SAVED-001",
                    Orders = new List<LaundryOrderDto> { expectedOrder }
                }
            };

            var loginApi = new InMemoryTaxLoginApiClient();
            var controller = CreateController(customerApi, loginApi);
            SetControllerSession(controller, "rksadhu11@gmail.com", "RTJ", "Admin");

            var result = await controller.GetCustomerOrders("Walk-in Customer", "CU-SAVED-001");

            var json = Assert.IsType<JsonResult>(result);
            var payload = JObject.FromObject(json.Value);
            Assert.True(payload.Value<bool>("success"));
            Assert.Equal("CU-SAVED-001", payload.Value<string>("customerCode"));

            var orders = payload["orders"] as JArray ?? new JArray();
            Assert.Single(orders);
            Assert.Equal("RTJ-1001", orders[0]?["OrderNo"]?.ToString());

            var orderQuery = customerApi.QueryCalls.Last(x => string.Equals(x.RequestUrl, "/GetLaundryOrders", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("rksadhu11@gmail.com", GetQueryValue(orderQuery.Query, "tenantName"));
            Assert.Equal("RTJ", GetQueryValue(orderQuery.Query, "storeCode"));
            Assert.Equal("CU-SAVED-001", GetQueryValue(orderQuery.Query, "custCode"));
        }

        [Fact]
        public void TenantSubscriptionPlans_Js_RenderSummary_UsesPersistedHistoryForPaidSuccessBanner()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TenantSubscriptionPlans.cshtml"));

            var content = File.ReadAllText(viewPath);
            var normalizeStatusFunction = ExtractFunctionBlock(content, "normalizeStatus");
            var isTrialStatusFunction = ExtractFunctionBlock(content, "isTrialStatus");
            var isExpiredStatusFunction = ExtractFunctionBlock(content, "isExpiredStatus");
            var statusClassFunction = ExtractFunctionBlock(content, "statusClass");
            var toDateTextFunction = ExtractFunctionBlock(content, "toDateText");
            var formatAmountFunction = ExtractFunctionBlock(content, "formatAmount");
            var findRenewalPlanFunction = ExtractFunctionBlock(content, "findRenewalPlan");
            var renewalCycleSuffixFunction = ExtractFunctionBlock(content, "renewalCycleSuffix");
            var daysRemainingFunction = ExtractFunctionBlock(content, "daysRemaining");
            var hasConfirmedPaidStateFunction = ExtractFunctionBlock(content, "hasConfirmedPaidState");
            var renderSummaryFunction = ExtractFunctionBlock(content, "renderSummary");

            var script = $@"
                function el() {{ return {{ textContent: '', className: '', style: {{ display: '' }}, classList: {{ add: function(c) {{ this._c = c; }} }} }}; }}
                var persistentStatusMessage = el();
                var currentPlanValue = el();
                var currentBillingCycleValue = el();
                var subscriptionEndValue = el();
                var renewalAmountValue = el();
                var currentStatusBadge = el();
                var trialInfoCard = el();
                var trialEndValue = el();
                var trialDaysRemainingValue = el();
                var summaryInfoMessage = el();

                function setPersistentStatusMessage(type, text) {{
                    persistentStatusMessage.className = 'subscription-msg';
                    persistentStatusMessage.textContent = text || '';
                    if (!text) return;
                    persistentStatusMessage.className = persistentStatusMessage.className + ' ' + (type === 'success' ? 'success' : 'error');
                }}

                {normalizeStatusFunction}
                {isTrialStatusFunction}
                {isExpiredStatusFunction}
                {statusClassFunction}
                {toDateTextFunction}
                {formatAmountFunction}
                {findRenewalPlanFunction}
                {renewalCycleSuffixFunction}
                {daysRemainingFunction}
                {hasConfirmedPaidStateFunction}
                {renderSummaryFunction}

                var overview = {{ planCode: 'Standard', billingCycle: 'Monthly', subscriptionStatus: 'Active' }};
                var plans = [{{ planCode: 'Standard', billingCycle: 'Monthly', amount: 499, currency: 'INR' }}];
                var history = [{{ planCode: 'Standard', billingCycle: 'Monthly', orderStatus: 'Paid' }}];
                renderSummary(overview, plans, history);
            ";

            var engine = new Engine();
            engine.Execute(script);

            Assert.Equal("Payment confirmed. Your subscription is active.", engine.Evaluate("persistentStatusMessage.textContent").ToString());
        }

        [Fact]
        public void TenantSubscriptionPlans_Js_VerifyCheckout_TreatsIsVerifiedTrueAsSuccessWithoutStatusCodeOrMessage()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TenantSubscriptionPlans.cshtml"));

            var content = File.ReadAllText(viewPath);
            var verifyCheckoutFunction = ExtractFunctionBlock(content, "verifyCheckout");

            var script = $@"
                var loadOverviewCount = 0;
                var lastMessageType = '';
                var lastMessageText = '';

                function URLSearchParams() {{
                    this._pairs = [];
                    this.append = function(k, v) {{ this._pairs.push([k, v]); }};
                    this.toString = function() {{
                        var encoded = this._pairs.map(function(p) {{ return encodeURIComponent(p[0]) + '=' + encodeURIComponent(p[1]); }});
                        return encoded.join('&');
                    }};
                }}

                function setMessage(type, text) {{
                    lastMessageType = type || '';
                    lastMessageText = text || '';
                }}

                function loadOverview() {{
                    loadOverviewCount++;
                }}

                function fetch() {{
                    return {{
                        then: function(onResponse) {{
                            var parsed = onResponse({{ json: function() {{ return {{ isVerified: true }}; }} }});
                            return {{
                                then: function(onData) {{
                                    onData(parsed);
                                    return {{
                                        catch: function() {{ return this; }},
                                        finally: function(onDone) {{ if (onDone) onDone(); return this; }}
                                    }};
                                }}
                            }};
                        }}
                    }};
                }}

                {verifyCheckoutFunction}
                verifyCheckout({{ paymentOrderId: 11, razorpayOrderId: 'order_1', razorpayPaymentId: 'pay_1', razorpaySignature: 'sig_1' }});
            ";

            var engine = new Engine();
            engine.Execute(script);

            Assert.Equal("success", engine.Evaluate("lastMessageType").ToString());
            Assert.Equal("Subscription payment verified successfully.", engine.Evaluate("lastMessageText").ToString());
            Assert.Equal("1", engine.Evaluate("String(loadOverviewCount)").ToString());
        }

        [Fact]
        public void TenantSubscriptionPlans_Js_VerifyCheckout_RefreshesOverviewWithForceFreshAndPreservedMessage()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TenantSubscriptionPlans.cshtml"));

            var content = File.ReadAllText(viewPath);
            var normalizeStatusFunction = ExtractFunctionBlock(content, "normalizeStatus");
            var hasConfirmedPaidStateFunction = ExtractFunctionBlock(content, "hasConfirmedPaidState");
            var verifyCheckoutFunction = ExtractFunctionBlock(content, "verifyCheckout");

            var script = $@"
                var __lastLoadOverviewOptions = null;
                var __lastSetTimeoutDelay = 0;
                var __loadOverviewCalls = 0;
                var __lastMessageType = '';
                var __lastMessageText = '';

                function URLSearchParams() {{
                    this._pairs = [];
                    this.append = function(k, v) {{ this._pairs.push([k, v]); }};
                    this.toString = function() {{
                        var encoded = this._pairs.map(function(p) {{ return encodeURIComponent(p[0]) + '=' + encodeURIComponent(p[1]); }});
                        return encoded.join('&');
                    }};
                }}

                function setMessage(type, text) {{
                    __lastMessageType = type || '';
                    __lastMessageText = text || '';
                }}

                function loadOverview(options) {{
                    __lastLoadOverviewOptions = options || null;
                    __loadOverviewCalls++;
                    return {{
                        then: function(resolve) {{
                            resolve({{ overview: {{ subscriptionStatus: 'Active', planCode: 'Standard', billingCycle: 'Monthly' }}, history: [{{ orderStatus: 'Paid', planCode: 'Standard', billingCycle: 'Monthly' }}] }});
                            return {{
                                catch: function() {{ return this; }}
                            }};
                        }}
                    }};
                }}

                function setTimeout(fn, delay) {{
                    __lastSetTimeoutDelay = delay;
                    if (fn) fn();
                    return 1;
                }}

                function fetch() {{
                    return {{
                        then: function(onResponse) {{
                            var parsed = onResponse({{ json: function() {{ return {{ isVerified: true }}; }} }});
                            return {{
                                then: function(onData) {{
                                    onData(parsed);
                                    return {{
                                        catch: function() {{ return this; }},
                                        finally: function(onDone) {{ if (onDone) onDone(); return this; }}
                                    }};
                                }}
                            }};
                        }}
                    }};
                }}

                {normalizeStatusFunction}
                {hasConfirmedPaidStateFunction}
                {verifyCheckoutFunction}

                verifyCheckout({{ paymentOrderId: 42, razorpayOrderId: 'order_ok', razorpayPaymentId: 'pay_ok', razorpaySignature: 'sig_ok' }});
            ";

            var engine = new Engine();
            engine.Execute(script);

            Assert.Equal("success", engine.Evaluate("__lastMessageType").ToString());
            Assert.Equal("Subscription payment verified successfully.", engine.Evaluate("__lastMessageText").ToString());
            Assert.Equal("1", engine.Evaluate("String(__loadOverviewCalls)").ToString());
            Assert.Equal("true", engine.Evaluate("String(!!__lastLoadOverviewOptions && __lastLoadOverviewOptions.forceFresh === true)").ToString());
            Assert.Equal("true", engine.Evaluate("String(!!__lastLoadOverviewOptions && __lastLoadOverviewOptions.preserveTransientMessage === true)").ToString());
        }

        [Fact]
        public void TenantSubscriptionPlans_Js_StartCheckout_OnDismiss_ShowsCancellationMessage_AndKeepsSessionFlowOnPage()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TenantSubscriptionPlans.cshtml"));

            var content = File.ReadAllText(viewPath);
            var startCheckoutFunction = ExtractFunctionBlock(content, "startCheckout");

            var script = $@"
                var __lastMessageType = '';
                var __lastMessageText = '';
                var __capturedOptions = null;

                function URLSearchParams() {{
                    this._pairs = [];
                    this.append = function(k, v) {{ this._pairs.push([k, v]); }};
                    this.toString = function() {{
                        var encoded = this._pairs.map(function(p) {{ return encodeURIComponent(p[0]) + '=' + encodeURIComponent(p[1]); }});
                        return encoded.join('&');
                    }};
                }}

                function setMessage(type, text) {{
                    __lastMessageType = type || '';
                    __lastMessageText = text || '';
                }}

                function verifyCheckout() {{ }}

                function Razorpay(options) {{
                    __capturedOptions = options;
                    this.on = function() {{ }};
                    this.open = function() {{ }};
                }}

                function fetch() {{
                    return {{
                        then: function(onResponse) {{
                            var parsed = onResponse({{ json: function() {{ return {{
                                success: true,
                                order: {{
                                    paymentOrderId: 41,
                                    razorpayOrderId: 'order_41',
                                    razorpayKeyId: 'rzp_test_123',
                                    amountMinor: 49900,
                                    currency: 'INR',
                                    planCode: 'Standard',
                                    billingCycle: 'Monthly'
                                }}
                            }}; }} }});

                            return {{
                                then: function(onData) {{
                                    onData(parsed);
                                    return {{
                                        catch: function() {{ return this; }}
                                    }};
                                }}
                            }};
                        }}
                    }};
                }}

                {startCheckoutFunction}

                var canCheckout = true;
                var btn = {{ disabled: false }};
                startCheckout('Standard', 'Monthly', btn);
                __capturedOptions.modal.ondismiss();
            ";

            var engine = new Engine();
            engine.Execute(script);

            Assert.Equal("error", engine.Evaluate("__lastMessageType").ToString());
            Assert.Equal("Payment cancelled. No subscription changes were made.", engine.Evaluate("__lastMessageText").ToString());
            Assert.Equal("false", engine.Evaluate("String(btn.disabled)").ToString());
        }

        [Fact]
        public void TenantSubscriptionPlans_Js_LoadOverview_SessionExpiredWithRedirect_OnlyThenNavigatesToLogin()
        {
            var viewPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "LMSWebUI",
                "Views",
                "Customer",
                "TenantSubscriptionPlans.cshtml"));

            var content = File.ReadAllText(viewPath);
            var loadOverviewFunction = ExtractFunctionBlock(content, "loadOverview");

            var script = $@"
                var __lastMessageType = '';
                var __lastMessageText = '';
                var plansHost = {{ textContent: '' }};
                var historyHost = {{ textContent: '' }};
                var window = {{ location: {{ href: '' }} }};

                function setMessage(type, text) {{
                    __lastMessageType = type || '';
                    __lastMessageText = text || '';
                }}

                function setPersistentStatusMessage() {{ }}
                function renderSummary() {{ }}
                function renderPlans() {{ }}
                function renderHistory() {{ }}

                function fetch() {{
                    return {{
                        then: function(onResponse) {{
                            var parsed = onResponse({{ json: function() {{ return {{
                                success: false,
                                message: 'Session expired. Please log in again.',
                                redirectUrl: '/Login/Login'
                            }}; }} }});

                            return {{
                                then: function(onData) {{
                                    onData(parsed);
                                    return {{
                                        catch: function() {{ return this; }}
                                    }};
                                }}
                            }};
                        }}
                    }};
                }}

                {loadOverviewFunction}
                loadOverview();
            ";

            var engine = new Engine();
            engine.Execute(script);

            Assert.Equal("error", engine.Evaluate("__lastMessageType").ToString());
            Assert.Equal("Session expired. Please log in again.", engine.Evaluate("__lastMessageText").ToString());
            Assert.Equal("/Login/Login", engine.Evaluate("window.location.href").ToString());
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
            var storeCfg = new FakeStoreConfigService(
                new StoreConfigurationViewModel
                {
                    CurrencyCode = "INR",
                    CurrencySymbol = "₹",
                    CurrencyLocale = "en-IN",
                    CurrencyDecimalDigits = 2
                },
                new StoreConfigurationViewModel
                {
                    CurrencyCode = "INR",
                    CurrencySymbol = "₹",
                    CurrencyLocale = "en-IN",
                    CurrencyDecimalDigits = 2
                });
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
                var taxMode = 'GST';
                var taxComponents = [];
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
                    }},
                    querySelectorAll: function () {{ return []; }}
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
            var normalizeTextFunction = ExtractFunctionBlock(content, "normalizeText");
            var equalsNormalizedFunction = ExtractFunctionBlock(content, "equalsNormalized");
            var getStatusTokenFunction = ExtractFunctionBlock(content, "getStatusToken");
            var isActiveRecordFunction = ExtractFunctionBlock(content, "isActiveRecord");
            var isScopedRecordFunction = ExtractFunctionBlock(content, "isScopedRecord");
            var filterActiveScopedRowsFunction = ExtractFunctionBlock(content, "filterActiveScopedRows");
            var buildServiceCategoryKeyFunction = ExtractFunctionBlock(content, "buildServiceCategoryKey");
            var buildServiceCategoryItemKeyFunction = ExtractFunctionBlock(content, "buildServiceCategoryItemKey");
            var rebuildActiveMasterLookupsFunction = ExtractFunctionBlock(content, "rebuildActiveMasterLookups");
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
                var configuredItems = [];
                var configuredItemsRaw = [];
                var configuredItemsLoaded = false;
                var storeMastersLoaded = false;
                var storeMastersFailed = false;
                var currentTenantName = '';
                var currentStoreCode = '';
                var activeMasterServiceLookup = {{}};
                var activeMasterCategoryLookup = {{}};
                var activeMasterItemLookup = {{}};
                var activeServiceTypes = [];
                var selectedService = '';
                var selectedCategory = '';
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
                function applyConfiguredItemHierarchyFilter() {{ }}
                function findMatchingValue(list, value) {{
                    if (!list || !value) return '';
                    var normalized = value.toString().trim().toLowerCase();
                    for (var i = 0; i < list.length; i++) {{
                        var candidate = (list[i] || '').toString().trim();
                        if (candidate.toLowerCase() === normalized) return candidate;
                    }}
                    return '';
                }}

                {normalizeTextFunction}
                {equalsNormalizedFunction}
                {getStatusTokenFunction}
                {isActiveRecordFunction}
                {isScopedRecordFunction}
                {filterActiveScopedRowsFunction}
                {buildServiceCategoryKeyFunction}
                {buildServiceCategoryItemKeyFunction}
                {rebuildActiveMasterLookupsFunction}
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
            public PaymentSettingsViewModel PaymentSettingsResponse { get; set; }
            public PaymentSettingsViewModel LastSavedPaymentSettings { get; private set; }
            public int SavePaymentSettingsCount { get; private set; }
            public int InsertCustomerCount { get; private set; }
            public JObject LastInsertedCustomerRequest { get; private set; }
            public List<string> StoredCustomerNames { get; } = new List<string>();
            public Dictionary<string, CustomerIOResponse> CustomersByName { get; } = new Dictionary<string, CustomerIOResponse>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<string>> SearchCustomersByText { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public JObject CreateLaundryOrderResponse { get; set; }
            public LaundryOrderResponseDto LaundryOrdersResponse { get; set; }
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

                if (string.Equals(requestUrl, "/SavePaymentSettings", StringComparison.OrdinalIgnoreCase))
                {
                    SavePaymentSettingsCount++;
                    LastSavedPaymentSettings = ToObject<PaymentSettingsViewModel>(model);
                    PaymentSettingsResponse = LastSavedPaymentSettings;

                    var response = new CustomerIOResponse
                    {
                        StatusCode = "200",
                        Message = "Payment settings saved."
                    };

                    return Task.FromResult(ToGeneric<T>(response));
                }

                if (string.Equals(requestUrl, "/InsertCustomer", StringComparison.OrdinalIgnoreCase))
                {
                    InsertCustomerCount++;
                    LastInsertedCustomerRequest = model != null
                        ? JObject.Parse(JsonConvert.SerializeObject(model))
                        : new JObject();

                    var customerName = Convert.ToString(LastInsertedCustomerRequest["CustomerName"])?.Trim();
                    var tenantName = Convert.ToString(LastInsertedCustomerRequest["TenantName"])?.Trim();
                    var storeCode = Convert.ToString(LastInsertedCustomerRequest["StoreCode"])?.Trim();
                    var normalizedName = (customerName ?? string.Empty).ToLowerInvariant();

                    if (!string.IsNullOrWhiteSpace(customerName)
                        && !StoredCustomerNames.Any(x => string.Equals(x, customerName, StringComparison.OrdinalIgnoreCase)))
                    {
                        StoredCustomerNames.Add(customerName);
                    }

                    var response = new CustomerIOResponse
                    {
                        StatusCode = "200",
                        Message = "Customer added successfully.",
                        CustomerName = customerName,
                        CustCode = $"CUS-{InsertCustomerCount:000}",
                        TenantName = tenantName
                    };

                    if (!string.IsNullOrWhiteSpace(customerName))
                    {
                        CustomersByName[normalizedName] = new CustomerIOResponse
                        {
                            StatusCode = "200",
                            CustomerName = customerName,
                            CustCode = response.CustCode,
                            TenantName = tenantName,
                            Storecodes = string.IsNullOrWhiteSpace(storeCode)
                                ? new List<string>()
                                : new List<string> { storeCode }
                        };
                    }

                    return Task.FromResult(ToGeneric<T>(response));
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

                if (string.Equals(requestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase)
                    && typeof(T) == typeof(JObject))
                {
                    var payload = CreateLaundryOrderResponse ?? new JObject();
                    return Task.FromResult((T)(object)payload);
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

                if (string.Equals(requestUrl, "/GetPaymentSettings", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(PaymentSettingsResponse));
                }

                if (string.Equals(requestUrl, "/CreateLaundryOrder", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(CreateLaundryOrderResponse));
                }

                if (string.Equals(requestUrl, "/GetCustomerByName", StringComparison.OrdinalIgnoreCase))
                {
                    var customerName = queryParams != null && queryParams.TryGetValue("customerName", out var customerNameValue)
                        ? customerNameValue?.Trim()
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(customerName)
                        && CustomersByName.TryGetValue(customerName.ToLowerInvariant(), out var customer))
                    {
                        return Task.FromResult(ToGeneric<T>(customer));
                    }

                    return Task.FromResult(ToGeneric<T>(new CustomerIOResponse
                    {
                        StatusCode = "404",
                        Message = "Customer not found in the current store."
                    }));
                }

                if (string.Equals(requestUrl, "/SearchCustomers", StringComparison.OrdinalIgnoreCase))
                {
                    var searchText = queryParams != null && queryParams.TryGetValue("searchText", out var value)
                        ? value?.Trim() ?? string.Empty
                        : string.Empty;

                    if (SearchCustomersByText.TryGetValue(searchText.ToLowerInvariant(), out var configuredNames))
                    {
                        return Task.FromResult(ToGeneric<T>(new CustomerIOResponse { CustomerNames = configuredNames }));
                    }

                    var names = StoredCustomerNames
                        .Where(x => x != null && x.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return Task.FromResult(ToGeneric<T>(new CustomerIOResponse { CustomerNames = names }));
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

                if (string.Equals(requestUrl, "/GetLaundryOrders", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(LaundryOrdersResponse));
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
            public JObject TenantStoreDetailsResponse { get; set; }
            public JObject TenantSubscriptionOverviewResponse { get; set; }
            public JObject StoreStatusesResponse { get; set; }

            public Task<T> SendRequestAsync<T>(string requestUrl, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, object model, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, List<Parameter> parameters, Method method) where T : new()
                => Task.FromResult(new T());

            public Task<T> SendRequestAsync<T>(string requestUrl, Dictionary<string, string> queryParams, Method method) where T : new()
            {
                if (string.Equals(requestUrl, "/GetTenantStoreDetails", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(TenantStoreDetailsResponse));
                }

                if (string.Equals(requestUrl, "/GetTenantSubscriptionOverview", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(TenantSubscriptionOverviewResponse));
                }

                if (string.Equals(requestUrl, "/GetAllStoreStatuses", StringComparison.OrdinalIgnoreCase)
                    && typeof(T) == typeof(JObject))
                {
                    var fallbackStatuses = StoreStatusesResponse
                        ?? TenantStoreDetailsResponse
                        ?? JObject.Parse(@"{
                            'StoreStatuses': [
                                { 'StoreCode': 'S1', 'TenantName': 'tenant@example.com', 'IsActive': true }
                            ]
                        }");

                    return Task.FromResult((T)(object)fallbackStatuses);
                }

                if (string.Equals(requestUrl, "/TenantprofileDetails", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(ToGeneric<T>(ProfileResponse));
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
        }
    }
}
