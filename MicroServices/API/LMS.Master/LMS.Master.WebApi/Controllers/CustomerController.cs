using System.Threading.Tasks;
using System;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Master.WebApi.Controllers
{
    [Route("Customer")]
    [ApiController]
    public class CustomerController : ControllerBase
    {
        private readonly ICustomer _customer;

        public CustomerController(ICustomer customer)
        {
            _customer = customer;
        }

        /// <summary>
        /// Test Service
        /// </summary>
        /// <returns></returns>
        [HttpGet(nameof(TestService))]
        public string TestService()
        {
            return "I am Live";
        }

        /// <summary>
        /// New Customer
        /// </summary>
        /// <returns></returns>
        [HttpPost(nameof(InsertCustomer))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> InsertCustomer([FromBody] CustomerDto customerDto)
        {
            if (customerDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var insertCustomerResult = await _customer.AddCustomer(customerDto);
            return StatusCode((int)insertCustomerResult.StatusCode, insertCustomerResult.ResultSet);
        }

        /// <summary>
        /// Get Customers
        /// </summary>
        /// <returns></returns>
        [HttpGet(nameof(GetCustomers))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public ActionResult GetCustomers(string eMail, string tenantName, string storeCode)
        {
            var getCustomersResult = _customer.GetCustomers(tenantName, storeCode);
            return StatusCode((int)getCustomersResult.StatusCode, getCustomersResult.ResultSet);
        }

        [HttpGet(nameof(SearchCustomers))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public ActionResult SearchCustomers(string searchText, string tenantName, string storeCode)
        {
            var searchCustomersResult = _customer.SearchCustomers(searchText, tenantName, storeCode);
            return StatusCode((int)searchCustomersResult.StatusCode, searchCustomersResult.ResultSet);
        }

        [HttpGet(nameof(GetCustomerByName))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public ActionResult GetCustomerByName(string customerName, string tenantName, string storeCode)
        {
            var customerResult = _customer.GetCustomerByName(customerName, tenantName, storeCode);
            return StatusCode((int)customerResult.StatusCode, customerResult.ResultSet);
        }

        [HttpPut(nameof(UpdateCustomer))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> UpdateCustomer([FromBody] CustomerDto customerDto)
        {
            if (customerDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var updateResult = await _customer.UpdateCustomer(customerDto);
            return StatusCode((int)updateResult.StatusCode, updateResult.ResultSet);
        }

        [HttpDelete(nameof(DeleteCustomer))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> DeleteCustomer(string custCode)
        {
            var deleteResult = await _customer.DeleteCustomer(custCode);
            return StatusCode((int)deleteResult.StatusCode, deleteResult.ResultSet);
        }

        [HttpGet(nameof(GetCustomerPreferences))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerPreferenceDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetCustomerPreferences(string tenantName, string storeCode)
        {
            var preferenceResult = await _customer.GetCustomerPreferences(tenantName, storeCode);
            return StatusCode((int)preferenceResult.StatusCode, preferenceResult.ResultSet);
        }

        [HttpPost(nameof(SaveCustomerPreferences))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveCustomerPreferences([FromBody] CustomerPreferenceDto preferenceDto)
        {
            if (preferenceDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var preferenceResult = await _customer.SaveCustomerPreferences(preferenceDto);
            return StatusCode((int)preferenceResult.StatusCode, preferenceResult.ResultSet);
        }

        [HttpGet(nameof(GetPricingRules))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<PricingRulesDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetPricingRules(string tenantName, string storeCode)
        {
            if (!TryResolvePricingScope(tenantName, storeCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            var pricingRulesResult = await _customer.GetPricingRules(scopedTenantName, scopedStoreCode);
            return StatusCode((int)pricingRulesResult.StatusCode, pricingRulesResult.ResultSet);
        }

        [HttpPost(nameof(SavePricingRules))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SavePricingRules([FromBody] PricingRulesDto pricingRulesDto)
        {
            if (pricingRulesDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            if (!TryResolvePricingScope(pricingRulesDto.TenantName, pricingRulesDto.StoreCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            pricingRulesDto.TenantName = scopedTenantName;
            pricingRulesDto.StoreCode = scopedStoreCode;

            var pricingRulesResult = await _customer.SavePricingRules(pricingRulesDto);
            return StatusCode((int)pricingRulesResult.StatusCode, pricingRulesResult.ResultSet);
        }

        private bool TryResolvePricingScope(string requestedTenantName, string requestedStoreCode, out string resolvedTenantName, out string resolvedStoreCode, out string validationError)
        {
            resolvedTenantName = requestedTenantName?.Trim();
            resolvedStoreCode = requestedStoreCode?.Trim();

            if (string.IsNullOrWhiteSpace(resolvedTenantName) || string.IsNullOrWhiteSpace(resolvedStoreCode))
            {
                validationError = "Invalid data for this operation.";
                return false;
            }

            var claimTenant = User?.FindFirst("tenantName")?.Value?.Trim();
            var claimStore = User?.FindFirst("storeCode")?.Value?.Trim();
            var headerTenant = Request?.Headers["X-TenantName"].ToString()?.Trim();
            var headerStore = Request?.Headers["X-StoreCode"].ToString()?.Trim();

            if (!string.IsNullOrWhiteSpace(claimTenant)
                && !string.Equals(claimTenant, resolvedTenantName, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Unauthorized tenant access.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(claimStore)
                && !string.Equals(claimStore, resolvedStoreCode, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Unauthorized store access.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(headerTenant)
                && !string.Equals(headerTenant, resolvedTenantName, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Tenant scope mismatch.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(headerStore)
                && !string.Equals(headerStore, resolvedStoreCode, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Store scope mismatch.";
                return false;
            }

            resolvedTenantName = !string.IsNullOrWhiteSpace(claimTenant) ? claimTenant : resolvedTenantName;
            resolvedStoreCode = !string.IsNullOrWhiteSpace(claimStore) ? claimStore : resolvedStoreCode;

            validationError = null;
            return true;
        }

        [HttpGet(nameof(GetPaymentSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<PaymentSettingsDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetPaymentSettings(string tenantName, string storeCode)
        {
            var paymentSettingsResult = await _customer.GetPaymentSettings(tenantName, storeCode);
            return StatusCode((int)paymentSettingsResult.StatusCode, paymentSettingsResult.ResultSet);
        }

        [HttpPost(nameof(SavePaymentSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SavePaymentSettings([FromBody] PaymentSettingsDto paymentSettingsDto)
        {
            if (paymentSettingsDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var paymentSettingsResult = await _customer.SavePaymentSettings(paymentSettingsDto);
            return StatusCode((int)paymentSettingsResult.StatusCode, paymentSettingsResult.ResultSet);
        }

        [HttpGet(nameof(GetTaxInvoiceSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<TaxInvoiceSettingsDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetTaxInvoiceSettings(string tenantName, string storeCode)
        {
            var taxInvoiceSettingsResult = await _customer.GetTaxInvoiceSettings(tenantName, storeCode);
            return StatusCode((int)taxInvoiceSettingsResult.StatusCode, taxInvoiceSettingsResult.ResultSet);
        }

        [HttpPost(nameof(SaveTaxInvoiceSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveTaxInvoiceSettings([FromBody] TaxInvoiceSettingsDto taxInvoiceSettingsDto)
        {
            if (taxInvoiceSettingsDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var taxInvoiceSettingsResult = await _customer.SaveTaxInvoiceSettings(taxInvoiceSettingsDto);
            return StatusCode((int)taxInvoiceSettingsResult.StatusCode, taxInvoiceSettingsResult.ResultSet);
        }

        [HttpGet(nameof(GetWorkflowStatusSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<WorkflowStatusSettingsDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetWorkflowStatusSettings(string tenantName, string storeCode)
        {
            var workflowStatusResult = await _customer.GetWorkflowStatusSettings(tenantName, storeCode);
            return StatusCode((int)workflowStatusResult.StatusCode, workflowStatusResult.ResultSet);
        }

        [HttpPost(nameof(SaveWorkflowStatusSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveWorkflowStatusSettings([FromBody] WorkflowStatusSettingsDto workflowStatusSettingsDto)
        {
            if (workflowStatusSettingsDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var workflowStatusResult = await _customer.SaveWorkflowStatusSettings(workflowStatusSettingsDto);
            return StatusCode((int)workflowStatusResult.StatusCode, workflowStatusResult.ResultSet);
        }

        [HttpGet(nameof(GetBarcodeTagSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<BarcodeTagSettingsDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetBarcodeTagSettings(string tenantName, string storeCode)
        {
            var barcodeTagResult = await _customer.GetBarcodeTagSettings(tenantName, storeCode);
            return StatusCode((int)barcodeTagResult.StatusCode, barcodeTagResult.ResultSet);
        }

        [HttpPost(nameof(SaveBarcodeTagSettings))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveBarcodeTagSettings([FromBody] BarcodeTagSettingsDto barcodeTagSettingsDto)
        {
            if (barcodeTagSettingsDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var barcodeTagResult = await _customer.SaveBarcodeTagSettings(barcodeTagSettingsDto);
            return StatusCode((int)barcodeTagResult.StatusCode, barcodeTagResult.ResultSet);
        }

        [HttpPost(nameof(SaveCustomerAdvance))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveCustomerAdvance([FromBody] CustomerAdvanceDto advanceDto)
        {
            if (advanceDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var advanceResult = await _customer.SaveCustomerAdvance(advanceDto);
            return StatusCode((int)advanceResult.StatusCode, advanceResult.ResultSet);
        }

        [HttpGet(nameof(GetCustomerAdvances))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerAdvanceResponseDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetCustomerAdvances(string tenantName, string storeCode, string custCode)
        {
            var advanceResult = await _customer.GetCustomerAdvances(tenantName, storeCode, custCode);
            return StatusCode((int)advanceResult.StatusCode, advanceResult.ResultSet);
        }

        [HttpPost(nameof(CreateLaundryOrder))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryOrderDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> CreateLaundryOrder([FromBody] LaundryOrderDto orderDto)
        {
            if (orderDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var orderResult = await _customer.CreateLaundryOrder(orderDto);
            return StatusCode((int)orderResult.StatusCode, orderResult.ResultSet);
        }

        [HttpGet(nameof(GetLaundryOrders))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryOrderResponseDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetLaundryOrders(string tenantName, string storeCode, string custCode)
        {
            var orderResult = await _customer.GetLaundryOrders(tenantName, storeCode, custCode);
            return StatusCode((int)orderResult.StatusCode, orderResult.ResultSet);
        }

        [HttpGet(nameof(GetStoreLaundryOrders))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryOrderResponseDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetStoreLaundryOrders(string tenantName, string storeCode, string status = null, string searchText = null)
        {
            var orderResult = await _customer.GetStoreLaundryOrders(tenantName, storeCode, status, searchText);
            return StatusCode((int)orderResult.StatusCode, orderResult.ResultSet);
        }

        [HttpPost(nameof(UpdateLaundryOrderStatus))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> UpdateLaundryOrderStatus(string tenantName, string storeCode, string orderNo, string status)
        {
            var orderResult = await _customer.UpdateLaundryOrderStatus(tenantName, storeCode, orderNo, status);
            return StatusCode((int)orderResult.StatusCode, orderResult.ResultSet);
        }

        [HttpPost(nameof(SettleLaundryOrderPayment))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryOrderDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SettleLaundryOrderPayment(string tenantName, string storeCode, string orderNo, decimal paidAmount, string paymentMode = null, string notes = null)
        {
            var orderResult = await _customer.SettleLaundryOrderPayment(tenantName, storeCode, orderNo, paidAmount, paymentMode, notes);
            return StatusCode((int)orderResult.StatusCode, orderResult.ResultSet);
        }

        [HttpGet(nameof(GetLaundryDeliveryStatus))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryDeliveryStatusResponseDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetLaundryDeliveryStatus(string tenantName, string storeCode)
        {
            var result = await _customer.GetLaundryDeliveryStatus(tenantName, storeCode);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(SaveLaundryItemPrice))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveLaundryItemPrice([FromBody] LaundryItemPriceDto itemPriceDto)
        {
            if (itemPriceDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            if (!TryResolveTenantStoreScope(itemPriceDto.TenantName, itemPriceDto.StoreCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            itemPriceDto.TenantName = scopedTenantName;
            itemPriceDto.StoreCode = scopedStoreCode;

            var result = await _customer.SaveLaundryItemPrice(itemPriceDto);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(DeactivateLaundryItemPrice))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> DeactivateLaundryItemPrice([FromBody] LaundryItemPriceDto itemPriceDto)
        {
            if (itemPriceDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            if (!TryResolveTenantStoreScope(itemPriceDto.TenantName, itemPriceDto.StoreCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            var result = await _customer.DeactivateLaundryItemPrice(
                scopedTenantName,
                scopedStoreCode,
                itemPriceDto.ServiceType,
                itemPriceDto.Category,
                itemPriceDto.ItemName);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(ReactivateLaundryItemPrice))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> ReactivateLaundryItemPrice([FromBody] LaundryItemPriceDto itemPriceDto)
        {
            if (itemPriceDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            if (!TryResolveTenantStoreScope(itemPriceDto.TenantName, itemPriceDto.StoreCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            var result = await _customer.ReactivateLaundryItemPrice(
                scopedTenantName,
                scopedStoreCode,
                itemPriceDto.ServiceType,
                itemPriceDto.Category,
                itemPriceDto.ItemName);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpGet(nameof(GetLaundryItemPrices))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LaundryItemPriceDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetLaundryItemPrices(string tenantName, string storeCode, bool includeInactive = false)
        {
            if (!TryResolveTenantStoreScope(tenantName, storeCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            var result = await _customer.GetLaundryItemPrices(scopedTenantName, scopedStoreCode, includeInactive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(ImportDefaultLaundryItemPrices))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> ImportDefaultLaundryItemPrices(string tenantName, string storeCode, bool overwriteExisting = false)
        {
            if (!TryResolveTenantStoreScope(tenantName, storeCode, out var scopedTenantName, out var scopedStoreCode, out var scopeValidationError))
            {
                return BadRequest(scopeValidationError);
            }

            var result = await _customer.ImportDefaultLaundryItemPrices(scopedTenantName, scopedStoreCode, overwriteExisting);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        private bool TryResolveTenantStoreScope(string requestedTenantName, string requestedStoreCode, out string resolvedTenantName, out string resolvedStoreCode, out string validationError)
        {
            resolvedTenantName = requestedTenantName?.Trim();
            resolvedStoreCode = requestedStoreCode?.Trim();

            if (string.IsNullOrWhiteSpace(resolvedTenantName) || string.IsNullOrWhiteSpace(resolvedStoreCode))
            {
                validationError = "Invalid data for this operation.";
                return false;
            }

            var claimTenant = User?.FindFirst("tenantName")?.Value?.Trim();
            var claimStore = User?.FindFirst("storeCode")?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(claimTenant)
                && !string.Equals(claimTenant, resolvedTenantName, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Unauthorized tenant access.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(claimStore)
                && !string.Equals(claimStore, resolvedStoreCode, StringComparison.OrdinalIgnoreCase))
            {
                validationError = "Unauthorized store access.";
                return false;
            }

            resolvedTenantName = !string.IsNullOrWhiteSpace(claimTenant) ? claimTenant : resolvedTenantName;
            resolvedStoreCode = !string.IsNullOrWhiteSpace(claimStore) ? claimStore : resolvedStoreCode;

            validationError = null;
            return true;
        }

        [HttpPost(nameof(SaveStoreServiceMaster))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveStoreServiceMaster([FromBody] StoreServiceMasterDto masterDto)
        {
            if (masterDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var result = await _customer.SaveStoreServiceMaster(masterDto);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(SetStoreServiceMasterStatus))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SetStoreServiceMasterStatus([FromBody] StoreServiceMasterDto masterDto)
        {
            if (masterDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var result = await _customer.SetStoreServiceMasterStatus(
                masterDto.TenantName,
                masterDto.StoreCode,
                masterDto.MasterType,
                masterDto.Name,
                masterDto.IsActive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpGet(nameof(GetStoreServiceMasters))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<StoreServiceMasterDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetStoreServiceMasters(string tenantName, string storeCode, bool includeInactive = false)
        {
            var result = await _customer.GetStoreServiceMasters(tenantName, storeCode, includeInactive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(SaveStoreItemMaster))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SaveStoreItemMaster([FromBody] StoreItemMasterDto itemDto)
        {
            if (itemDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var result = await _customer.SaveStoreItemMaster(itemDto);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpPost(nameof(SetStoreItemMasterStatus))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> SetStoreItemMasterStatus([FromBody] StoreItemMasterDto itemDto)
        {
            if (itemDto == null)
            {
                return BadRequest("Invalid data for this operation.");
            }

            var result = await _customer.SetStoreItemMasterStatus(
                itemDto.TenantName,
                itemDto.StoreCode,
                itemDto.ServiceType,
                itemDto.Category,
                itemDto.ItemName,
                itemDto.IsActive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        [HttpGet(nameof(GetStoreItemMasters))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<StoreItemMasterDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetStoreItemMasters(string tenantName, string storeCode, bool includeInactive = false)
        {
            var result = await _customer.GetStoreItemMasters(tenantName, storeCode, includeInactive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }
    }
}