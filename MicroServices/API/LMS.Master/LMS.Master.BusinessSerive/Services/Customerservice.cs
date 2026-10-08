using AutoMapper;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using LMS.Master.Utilities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LMS.Master.BusinessSerive.Services
{
    public class Customerservice : ICustomer
    {
        private readonly IMapper _mapper;
        private readonly MasterDbContext _masterDbContext;
        private readonly List<string> _seedFilePaths;

        public Customerservice(IMapper mapper, MasterDbContext masterDbContext)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _masterDbContext = masterDbContext ?? throw new ArgumentNullException(nameof(masterDbContext));
            var contentRoot = AppContext.BaseDirectory;
            _seedFilePaths = new List<string>
            {
                Path.Combine(contentRoot, "SeedData", "LaundryItemDefaults.csv"),
                Path.Combine(AppContext.BaseDirectory, "SeedData", "LaundryItemDefaults.csv"),
                Path.Combine(contentRoot, "..", "LMS.Master.BusinessSerive", "SeedData", "LaundryItemDefaults.csv")
            };
        }

        /// <summary>
        /// Returns the customers that belong to the supplied tenant/store scope.
        /// Returns null when the scope is incomplete so callers can fail safe instead
        /// of falling back to a global (cross-store) query.
        /// </summary>
        private IQueryable<CustomerEntity> GetScopedCustomers(string tenantName, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            var normalizedTenantName = tenantName.Trim().ToLower();
            var normalizedStoreCode = storeCode.Trim().ToLower();

            return _masterDbContext.Customers
                .AsNoTracking()
                .Where(customer => !string.IsNullOrEmpty(customer.TenantName)
                                   && !string.IsNullOrEmpty(customer.StoreCode)
                                   && customer.TenantName.Trim().ToLower() == normalizedTenantName
                                   && customer.StoreCode.Trim().ToLower() == normalizedStoreCode);
        }

        public ActionReturnType GetCustomers(string tenantName, string storeCode)
        {
            var scopedCustomers = GetScopedCustomers(tenantName, storeCode);
            if (scopedCustomers == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse { StatusCode = "200", CustomerNames = new List<string>() });
            }

            var customerNames = scopedCustomers
                .Select(customer => customer.CustomerName)
                .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse { StatusCode = "200", CustomerNames = customerNames });
        }

        public ActionReturnType SearchCustomers(string searchText, string tenantName, string storeCode)
        {
            var normalizedSearchText = (searchText ?? string.Empty).Trim();
            var scopedCustomers = GetScopedCustomers(tenantName, storeCode);

            if (string.IsNullOrWhiteSpace(normalizedSearchText) || scopedCustomers == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse { StatusCode = "200", CustomerNames = new List<string>() });
            }

            var searchToken = normalizedSearchText.ToLower();
            var normalizedPhoneToken = new string(normalizedSearchText.Where(char.IsDigit).ToArray());

            var customerNames = scopedCustomers
                .Where(customer => !string.IsNullOrEmpty(customer.CustomerName)
                                   && (
                                       customer.CustomerName.ToLower().Contains(searchToken)
                                       || (!string.IsNullOrEmpty(normalizedPhoneToken)
                                           && !string.IsNullOrEmpty(customer.PhoneNumber)
                                           && customer.PhoneNumber
                                               .Replace(" ", string.Empty)
                                               .Replace("-", string.Empty)
                                               .Replace("(", string.Empty)
                                               .Replace(")", string.Empty)
                                               .Contains(normalizedPhoneToken))
                                   ))
                .OrderBy(customer => customer.CustomerName)
                .Select(customer => customer.CustomerName)
                .Distinct()
                .Take(25)
                .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                CustomerNames = customerNames
            });
        }

        public ActionReturnType GetCustomerByName(string customerName, string tenantName, string storeCode)
        {
            if (string.IsNullOrWhiteSpace(customerName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            var customers = GetScopedCustomers(tenantName, storeCode);
            if (customers == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse { StatusCode = "404", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            var normalizedName = customerName.Trim();
            var normalizedNameLower = normalizedName.ToLower();
            var normalizedDigits = new string(normalizedName.Where(char.IsDigit).ToArray());
            var isPhoneSearch = normalizedName.Length > 0
                                && normalizedName.All(char.IsDigit)
                                && normalizedDigits.Length >= 7;

            var customer = customers.FirstOrDefault(c =>
                (!string.IsNullOrWhiteSpace(c.CustomerName) && c.CustomerName.Trim().ToLower() == normalizedNameLower)
                || (!string.IsNullOrWhiteSpace(c.CustCode) && c.CustCode.Trim().ToLower() == normalizedNameLower)
                || (isPhoneSearch
                    && !string.IsNullOrWhiteSpace(c.PhoneNumber)
                    && c.PhoneNumber.Replace(" ", string.Empty)
                                    .Replace("-", string.Empty)
                                    .Replace("(", string.Empty)
                                    .Replace(")", string.Empty) == normalizedDigits));

            if (customer == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse { StatusCode = "404", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                CustCode = customer.CustCode,
                CustomerName = customer.CustomerName,
                Address = customer.Address,
                // MembershipId removed system-wide
                BarCode = customer.BarCode,
                PhoneNumber = customer.PhoneNumber,
                Email = customer.Email,
                TenantName = customer.TenantName,
                Storecodes = new List<string> { customer.StoreCode }
            });
        }

        public async Task<ActionReturnType> AddCustomer(CustomerDto customerDto)
        {
            if (customerDto == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NoContent, new CustomerIOResponse { StatusCode = "204", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            customerDto.TenantName = customerDto.TenantName?.Trim();
            customerDto.StoreCode = customerDto.StoreCode?.Trim();

            CustomerPreferenceEntity customerPreference = null;
            if (!string.IsNullOrWhiteSpace(customerDto.TenantName) && !string.IsNullOrWhiteSpace(customerDto.StoreCode))
            {
                await EnsureCustomerPreferencesTableExistsAsync();
                customerPreference = await _masterDbContext.CustomerPreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantName == customerDto.TenantName && x.StoreCode == customerDto.StoreCode);
            }

            if (string.IsNullOrWhiteSpace(customerDto.CustomerName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_NAME_REQUIRED_MESSAGE });
            }

            if (string.IsNullOrWhiteSpace(customerDto.Address))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_ADDRESS_REQUIRED_MESSAGE });
            }

            customerDto.CustomerName = customerDto.CustomerName.Trim();
            customerDto.Address = customerDto.Address.Trim();

            if (customerPreference != null && customerPreference.RequirePhoneNumber && string.IsNullOrWhiteSpace(customerDto.PhoneNumber))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = "Phone number is required as per customer preferences." });
            }

            if (customerPreference != null && customerPreference.RequireEmail && string.IsNullOrWhiteSpace(customerDto.Email))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = "Email is required as per customer preferences." });
            }

            if (!string.IsNullOrWhiteSpace(customerDto.PhoneNumber))
            {
                customerDto.PhoneNumber = customerDto.PhoneNumber.Trim().Replace(" ", string.Empty);
                if (!Regex.IsMatch(customerDto.PhoneNumber, @"^\d{7,15}$"))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_PHONE_INVALID_MESSAGE });
                }

                if (customerPreference != null && !customerPreference.AllowDuplicatePhoneNumber)
                {
                    var duplicatePhoneExists = await _masterDbContext.Customers.AnyAsync(c =>
                        !string.IsNullOrWhiteSpace(c.PhoneNumber)
                        && c.PhoneNumber.Trim().Replace(" ", string.Empty) == customerDto.PhoneNumber
                        && c.TenantName == customerDto.TenantName
                        && c.StoreCode == customerDto.StoreCode);

                    if (duplicatePhoneExists)
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse { StatusCode = "409", Message = "Phone number already exists for this store." });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(customerDto.Email))
            {
                customerDto.Email = customerDto.Email.Trim();
                if (!Regex.IsMatch(customerDto.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_EMAIL_INVALID_MESSAGE });
                }
            }

            var result = await _masterDbContext.Customers.FirstOrDefaultAsync(cu => cu.CustomerName == customerDto.CustomerName);
            if (result != null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.AlreadyReported, new CustomerIOResponse { StatusCode = "208", Message = CustomerValidationMessage.CUSTOMER_DATAFOUND_ERROR_MESSAGE });
            }

            var customerData = _mapper.Map<CustomerEntity>(customerDto);

            if (!string.IsNullOrWhiteSpace(customerDto.CustCode) && customerPreference != null && !customerPreference.AutoGenerateCustomerCode)
            {
                customerData.CustCode = customerDto.CustCode.Trim();
            }
            else
            {
                string customerCode = GenerateRandomID();
                customerData.CustCode = "Cust-" + customerCode;
            }

            customerData.CreatedDate = DateTime.UtcNow;
            customerData.ModifiedDate = DateTime.UtcNow;

            _masterDbContext.Customers.Add(customerData);
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.Created, new CustomerIOResponse { CustCode = customerData.CustCode, StatusCode = "200", Message = CustomerValidationMessage.CUSTOMER_INSERT_SUCCESS_MESSAGE });
        }

        public async Task<ActionReturnType> UpdateCustomer(CustomerDto customerDto)
        {
            if (customerDto == null || string.IsNullOrWhiteSpace(customerDto.CustCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            customerDto.CustCode = customerDto.CustCode.Trim();
            customerDto.TenantName = customerDto.TenantName?.Trim();
            customerDto.StoreCode = customerDto.StoreCode?.Trim();

            CustomerPreferenceEntity customerPreference = null;
            if (!string.IsNullOrWhiteSpace(customerDto.TenantName) && !string.IsNullOrWhiteSpace(customerDto.StoreCode))
            {
                await EnsureCustomerPreferencesTableExistsAsync();
                customerPreference = await _masterDbContext.CustomerPreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantName == customerDto.TenantName && x.StoreCode == customerDto.StoreCode);
            }

            if (string.IsNullOrWhiteSpace(customerDto.CustomerName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_NAME_REQUIRED_MESSAGE });
            }

            if (string.IsNullOrWhiteSpace(customerDto.Address))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_ADDRESS_REQUIRED_MESSAGE });
            }

            customerDto.CustomerName = customerDto.CustomerName.Trim();
            customerDto.Address = customerDto.Address.Trim();

            if (customerPreference != null && customerPreference.RequirePhoneNumber && string.IsNullOrWhiteSpace(customerDto.PhoneNumber))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = "Phone number is required as per customer preferences." });
            }

            if (customerPreference != null && customerPreference.RequireEmail && string.IsNullOrWhiteSpace(customerDto.Email))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = "Email is required as per customer preferences." });
            }

            if (!string.IsNullOrWhiteSpace(customerDto.PhoneNumber))
            {
                customerDto.PhoneNumber = customerDto.PhoneNumber.Trim().Replace(" ", string.Empty);
                if (!Regex.IsMatch(customerDto.PhoneNumber, @"^\d{7,15}$"))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_PHONE_INVALID_MESSAGE });
                }

                if (customerPreference != null && !customerPreference.AllowDuplicatePhoneNumber)
                {
                    var duplicatePhoneExists = await _masterDbContext.Customers.AnyAsync(c =>
                        c.CustCode != customerDto.CustCode
                        && c.TenantName == customerDto.TenantName
                        && c.StoreCode == customerDto.StoreCode
                        && !string.IsNullOrWhiteSpace(c.PhoneNumber)
                        && c.PhoneNumber.Trim().Replace(" ", string.Empty) == customerDto.PhoneNumber);

                    if (duplicatePhoneExists)
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse { StatusCode = "409", Message = "Phone number already exists for this store." });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(customerDto.Email))
            {
                customerDto.Email = customerDto.Email.Trim();
                if (!Regex.IsMatch(customerDto.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_EMAIL_INVALID_MESSAGE });
                }
            }

            var customer = await _masterDbContext.Customers.FirstOrDefaultAsync(c => c.CustCode == customerDto.CustCode);
            if (customer == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse { StatusCode = "404", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            if (!string.IsNullOrWhiteSpace(customerDto.CustomerName) && !string.Equals(customer.CustomerName, customerDto.CustomerName, StringComparison.OrdinalIgnoreCase))
            {
                var duplicate = await _masterDbContext.Customers.AnyAsync(c => c.CustomerName == customerDto.CustomerName && c.CustCode != customerDto.CustCode);
                if (duplicate)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse { StatusCode = "409", Message = CustomerValidationMessage.CUSTOMER_DATAFOUND_ERROR_MESSAGE });
                }
            }

            customer.CustomerName = customerDto.CustomerName;
            customer.Address = customerDto.Address;
            // MembershipId removed system-wide
            customer.BarCode = customerDto.BarCode;
            customer.PhoneNumber = customerDto.PhoneNumber;
            customer.Email = customerDto.Email;
            customer.TenantName = customerDto.TenantName;
            customer.StoreCode = customerDto.StoreCode;
            customer.ModifiedDate = DateTime.UtcNow;

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                CustCode = customer.CustCode,
                Message = CustomerValidationMessage.CUSTOMER_UPDATE_SUCCESS_MESSAGE
            });
        }

        public async Task<ActionReturnType> DeleteCustomer(string custCode)
        {
            if (string.IsNullOrWhiteSpace(custCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse { StatusCode = "400", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            custCode = custCode.Trim();

            var customer = await _masterDbContext.Customers.FirstOrDefaultAsync(c => c.CustCode == custCode);
            if (customer == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse { StatusCode = "404", Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE });
            }

            _masterDbContext.Customers.Remove(customer);
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                CustCode = custCode,
                Message = CustomerValidationMessage.CUSTOMER_DELETE_SUCCESS_MESSAGE
            });
        }

        public async Task<ActionReturnType> SaveCustomerPreferences(CustomerPreferenceDto preferenceDto)
        {
            await EnsureCustomerPreferencesTableExistsAsync();

            if (preferenceDto == null
                || string.IsNullOrWhiteSpace(preferenceDto.TenantName)
                || string.IsNullOrWhiteSpace(preferenceDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE
                });
            }

            if (preferenceDto.PickupReminderHours < 0 || preferenceDto.PickupReminderHours > 168)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Pickup reminder should be between 0 and 168 hours."
                });
            }

            if (preferenceDto.LoyaltyPointsPerOrder < 0 || preferenceDto.LoyaltyPointsPerOrder > 100)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Loyalty points per order should be between 0 and 100."
                });
            }

            preferenceDto.TenantName = preferenceDto.TenantName.Trim();
            preferenceDto.StoreCode = preferenceDto.StoreCode.Trim();

            var existing = await _masterDbContext.CustomerPreferences.FirstOrDefaultAsync(x =>
                x.TenantName == preferenceDto.TenantName && x.StoreCode == preferenceDto.StoreCode);

            if (existing == null)
            {
                var entity = _mapper.Map<CustomerPreferenceEntity>(preferenceDto);
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.CustomerPreferences.Add(entity);
            }
            else
            {
                existing.EnableSmsNotifications = preferenceDto.EnableSmsNotifications;
                existing.EnableEmailNotifications = preferenceDto.EnableEmailNotifications;
                existing.AutoGenerateCustomerCode = preferenceDto.AutoGenerateCustomerCode;
                existing.RequirePhoneNumber = preferenceDto.RequirePhoneNumber;
                existing.RequireEmail = preferenceDto.RequireEmail;
                existing.AllowDuplicatePhoneNumber = preferenceDto.AllowDuplicatePhoneNumber;
                existing.DefaultServiceType = preferenceDto.DefaultServiceType;
                existing.DefaultPaymentMode = preferenceDto.DefaultPaymentMode;
                existing.DefaultStarchLevel = preferenceDto.DefaultStarchLevel;
                existing.PickupReminderHours = preferenceDto.PickupReminderHours;
                existing.LoyaltyPointsPerOrder = preferenceDto.LoyaltyPointsPerOrder;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Customer preferences saved successfully."
            });
        }

        public async Task<ActionReturnType> GetCustomerPreferences(string tenantName, string storeCode)
        {
            await EnsureCustomerPreferencesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = CustomerValidationMessage.CUSTOMER_DATANOTFOUND_ERROR_MESSAGE
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var preference = await _masterDbContext.CustomerPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (preference == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Customer preferences not found."
                });
            }

            var responseDto = _mapper.Map<CustomerPreferenceDto>(preference);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        public async Task<ActionReturnType> SavePricingRules(PricingRulesDto pricingRulesDto)
        {
            await EnsurePricingRulesTableExistsAsync();

            if (pricingRulesDto == null
                || string.IsNullOrWhiteSpace(pricingRulesDto.TenantName)
                || string.IsNullOrWhiteSpace(pricingRulesDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid pricing rules request."
                });
            }

            if (pricingRulesDto.ExpressSurchargePercent < 0 || pricingRulesDto.ExpressSurchargePercent > 100)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Express surcharge should be between 0 and 100."
                });
            }

            if (pricingRulesDto.MinimumOrderAmount < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Minimum order amount should be 0 or greater."
                });
            }

            if (pricingRulesDto.StandardTurnaroundHours < 1 || pricingRulesDto.StandardTurnaroundHours > 240)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Standard turnaround should be between 1 and 240 hours."
                });
            }

            if (pricingRulesDto.ExpressTurnaroundHours < 1 || pricingRulesDto.ExpressTurnaroundHours > 240)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Express turnaround should be between 1 and 240 hours."
                });
            }

            pricingRulesDto.TenantName = pricingRulesDto.TenantName.Trim();
            pricingRulesDto.StoreCode = pricingRulesDto.StoreCode.Trim();
            pricingRulesDto.Notes = string.IsNullOrWhiteSpace(pricingRulesDto.Notes)
                ? null
                : pricingRulesDto.Notes.Trim();

            if (!string.IsNullOrWhiteSpace(pricingRulesDto.Notes) && pricingRulesDto.Notes.Length > 300)
            {
                pricingRulesDto.Notes = pricingRulesDto.Notes.Substring(0, 300);
            }

            var existing = await _masterDbContext.PricingRules.FirstOrDefaultAsync(x =>
                x.TenantName == pricingRulesDto.TenantName && x.StoreCode == pricingRulesDto.StoreCode);

            if (existing == null)
            {
                var entity = _mapper.Map<PricingRulesEntity>(pricingRulesDto);
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.PricingRules.Add(entity);
            }
            else
            {
                existing.EnableExpressSurcharge = pricingRulesDto.EnableExpressSurcharge;
                existing.ExpressSurchargePercent = pricingRulesDto.ExpressSurchargePercent;
                existing.EnableMinimumOrder = pricingRulesDto.EnableMinimumOrder;
                existing.MinimumOrderAmount = pricingRulesDto.MinimumOrderAmount;
                existing.StandardTurnaroundHours = pricingRulesDto.StandardTurnaroundHours;
                existing.ExpressTurnaroundHours = pricingRulesDto.ExpressTurnaroundHours;
                existing.RoundOffInvoiceTotal = pricingRulesDto.RoundOffInvoiceTotal;
                existing.Notes = pricingRulesDto.Notes;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Pricing rules saved successfully."
            });
        }

        public async Task<ActionReturnType> GetPricingRules(string tenantName, string storeCode)
        {
            await EnsurePricingRulesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid pricing rules request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var pricingRules = await _masterDbContext.PricingRules
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (pricingRules == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Pricing rules not found."
                });
            }

            var responseDto = _mapper.Map<PricingRulesDto>(pricingRules);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        public async Task<ActionReturnType> SavePaymentSettings(PaymentSettingsDto paymentSettingsDto)
        {
            await EnsurePaymentSettingsTableExistsAsync();

            if (paymentSettingsDto == null
                || string.IsNullOrWhiteSpace(paymentSettingsDto.TenantName)
                || string.IsNullOrWhiteSpace(paymentSettingsDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid payment settings request."
                });
            }

            if (!paymentSettingsDto.EnableCash
                && !paymentSettingsDto.EnableUpi
                && !paymentSettingsDto.EnableCard
                && !paymentSettingsDto.EnableWallet)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "At least one payment mode must be enabled."
                });
            }

            var defaultMode = string.IsNullOrWhiteSpace(paymentSettingsDto.DefaultPaymentMode)
                ? null
                : paymentSettingsDto.DefaultPaymentMode.Trim();

            var defaultModeEnabled = defaultMode != null
                && ((defaultMode.Equals("Cash", StringComparison.OrdinalIgnoreCase) && paymentSettingsDto.EnableCash)
                    || (defaultMode.Equals("UPI", StringComparison.OrdinalIgnoreCase) && paymentSettingsDto.EnableUpi)
                    || (defaultMode.Equals("Card", StringComparison.OrdinalIgnoreCase) && paymentSettingsDto.EnableCard)
                    || (defaultMode.Equals("Wallet", StringComparison.OrdinalIgnoreCase) && paymentSettingsDto.EnableWallet));

            if (!defaultModeEnabled)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Default payment mode must be one of the enabled payment modes."
                });
            }

            if (paymentSettingsDto.CreditLimitAmount < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Credit limit amount should be 0 or greater."
                });
            }

            if (!paymentSettingsDto.AllowCredit)
            {
                paymentSettingsDto.CreditLimitAmount = 0;
            }

            paymentSettingsDto.TenantName = paymentSettingsDto.TenantName.Trim();
            paymentSettingsDto.StoreCode = paymentSettingsDto.StoreCode.Trim();
            paymentSettingsDto.DefaultPaymentMode = defaultMode;
            paymentSettingsDto.Notes = string.IsNullOrWhiteSpace(paymentSettingsDto.Notes)
                ? null
                : paymentSettingsDto.Notes.Trim();

            if (!string.IsNullOrWhiteSpace(paymentSettingsDto.Notes) && paymentSettingsDto.Notes.Length > 300)
            {
                paymentSettingsDto.Notes = paymentSettingsDto.Notes.Substring(0, 300);
            }

            var existing = await _masterDbContext.PaymentSettings.FirstOrDefaultAsync(x =>
                x.TenantName == paymentSettingsDto.TenantName && x.StoreCode == paymentSettingsDto.StoreCode);

            if (existing == null)
            {
                var entity = _mapper.Map<PaymentSettingsEntity>(paymentSettingsDto);
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.PaymentSettings.Add(entity);
            }
            else
            {
                existing.EnableCash = paymentSettingsDto.EnableCash;
                existing.EnableUpi = paymentSettingsDto.EnableUpi;
                existing.EnableCard = paymentSettingsDto.EnableCard;
                existing.EnableWallet = paymentSettingsDto.EnableWallet;
                existing.DefaultPaymentMode = paymentSettingsDto.DefaultPaymentMode;
                existing.AllowPartialPayment = paymentSettingsDto.AllowPartialPayment;
                existing.AllowCredit = paymentSettingsDto.AllowCredit;
                existing.CreditLimitAmount = paymentSettingsDto.CreditLimitAmount;
                existing.RoundOffPayableAmount = paymentSettingsDto.RoundOffPayableAmount;
                existing.Notes = paymentSettingsDto.Notes;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Payment settings saved successfully."
            });
        }

        public async Task<ActionReturnType> GetPaymentSettings(string tenantName, string storeCode)
        {
            await EnsurePaymentSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid payment settings request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var paymentSettings = await _masterDbContext.PaymentSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (paymentSettings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Payment settings not found."
                });
            }

            var responseDto = _mapper.Map<PaymentSettingsDto>(paymentSettings);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        public async Task<ActionReturnType> SaveTaxInvoiceSettings(TaxInvoiceSettingsDto taxInvoiceSettingsDto)
        {
            await EnsureTaxInvoiceSettingsTableExistsAsync();

            if (taxInvoiceSettingsDto == null
                || string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.TenantName)
                || string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax and invoice settings request."
                });
            }

            if (taxInvoiceSettingsDto.GstPercent < 0 || taxInvoiceSettingsDto.GstPercent > 100)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "GST percentage should be between 0 and 100."
                });
            }

            if (taxInvoiceSettingsDto.NextInvoiceNumber < 1)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Next invoice number should be 1 or greater."
                });
            }

            if (taxInvoiceSettingsDto.InvoiceNumberPadding < 1 || taxInvoiceSettingsDto.InvoiceNumberPadding > 12)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invoice number padding should be between 1 and 12."
                });
            }

            if (!taxInvoiceSettingsDto.EnableTax)
            {
                taxInvoiceSettingsDto.GstPercent = 0;
            }

            taxInvoiceSettingsDto.TenantName = taxInvoiceSettingsDto.TenantName.Trim();
            taxInvoiceSettingsDto.StoreCode = taxInvoiceSettingsDto.StoreCode.Trim();
            taxInvoiceSettingsDto.InvoicePrefix = string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.InvoicePrefix)
                ? BuildStoreInvoicePrefix(taxInvoiceSettingsDto.StoreCode)
                : taxInvoiceSettingsDto.InvoicePrefix.Trim().ToUpperInvariant();
            taxInvoiceSettingsDto.Notes = string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.Notes)
                ? null
                : taxInvoiceSettingsDto.Notes.Trim();

            if (!string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.InvoicePrefix) && taxInvoiceSettingsDto.InvoicePrefix.Length > 16)
            {
                taxInvoiceSettingsDto.InvoicePrefix = taxInvoiceSettingsDto.InvoicePrefix.Substring(0, 16);
            }

            if (!string.IsNullOrWhiteSpace(taxInvoiceSettingsDto.Notes) && taxInvoiceSettingsDto.Notes.Length > 300)
            {
                taxInvoiceSettingsDto.Notes = taxInvoiceSettingsDto.Notes.Substring(0, 300);
            }

            var existing = await _masterDbContext.TaxInvoiceSettings.FirstOrDefaultAsync(x =>
                x.TenantName == taxInvoiceSettingsDto.TenantName && x.StoreCode == taxInvoiceSettingsDto.StoreCode);

            if (existing == null)
            {
                var entity = _mapper.Map<TaxInvoiceSettingsEntity>(taxInvoiceSettingsDto);
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.TaxInvoiceSettings.Add(entity);
            }
            else
            {
                existing.EnableTax = taxInvoiceSettingsDto.EnableTax;
                existing.GstPercent = taxInvoiceSettingsDto.GstPercent;
                existing.PricesIncludeTax = taxInvoiceSettingsDto.PricesIncludeTax;
                existing.InvoicePrefix = taxInvoiceSettingsDto.InvoicePrefix;
                existing.NextInvoiceNumber = taxInvoiceSettingsDto.NextInvoiceNumber;
                existing.InvoiceNumberPadding = taxInvoiceSettingsDto.InvoiceNumberPadding;
                existing.ResetInvoiceNumberYearly = taxInvoiceSettingsDto.ResetInvoiceNumberYearly;
                existing.Notes = taxInvoiceSettingsDto.Notes;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Tax and invoice settings saved successfully."
            });
        }

        public async Task<ActionReturnType> GetTaxInvoiceSettings(string tenantName, string storeCode)
        {
            await EnsureTaxInvoiceSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax and invoice settings request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var taxInvoiceSettings = await _masterDbContext.TaxInvoiceSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (taxInvoiceSettings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax and invoice settings not found."
                });
            }

            var responseDto = _mapper.Map<TaxInvoiceSettingsDto>(taxInvoiceSettings);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        public async Task<ActionReturnType> SaveBarcodeTagSettings(BarcodeTagSettingsDto barcodeTagSettingsDto)
        {
            await EnsureBarcodeTagSettingsTableExistsAsync();

            if (barcodeTagSettingsDto == null
                || string.IsNullOrWhiteSpace(barcodeTagSettingsDto.TenantName)
                || string.IsNullOrWhiteSpace(barcodeTagSettingsDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid barcode and tag settings request."
                });
            }

            if (barcodeTagSettingsDto.NextTagNumber < 1)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Next tag number should be 1 or greater."
                });
            }

            if (barcodeTagSettingsDto.TagNumberPadding < 1 || barcodeTagSettingsDto.TagNumberPadding > 12)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Tag number padding should be between 1 and 12."
                });
            }

            barcodeTagSettingsDto.TenantName = barcodeTagSettingsDto.TenantName.Trim();
            barcodeTagSettingsDto.StoreCode = barcodeTagSettingsDto.StoreCode.Trim();
            barcodeTagSettingsDto.TagPrefix = string.IsNullOrWhiteSpace(barcodeTagSettingsDto.TagPrefix)
                ? BuildStoreInvoicePrefix(barcodeTagSettingsDto.StoreCode)
                : barcodeTagSettingsDto.TagPrefix.Trim().ToUpperInvariant();
            barcodeTagSettingsDto.Notes = string.IsNullOrWhiteSpace(barcodeTagSettingsDto.Notes)
                ? null
                : barcodeTagSettingsDto.Notes.Trim();

            if (!string.IsNullOrWhiteSpace(barcodeTagSettingsDto.TagPrefix) && barcodeTagSettingsDto.TagPrefix.Length > 16)
            {
                barcodeTagSettingsDto.TagPrefix = barcodeTagSettingsDto.TagPrefix.Substring(0, 16);
            }

            if (!string.IsNullOrWhiteSpace(barcodeTagSettingsDto.Notes) && barcodeTagSettingsDto.Notes.Length > 300)
            {
                barcodeTagSettingsDto.Notes = barcodeTagSettingsDto.Notes.Substring(0, 300);
            }

            var existing = await _masterDbContext.BarcodeTagSettings.FirstOrDefaultAsync(x =>
                x.TenantName == barcodeTagSettingsDto.TenantName && x.StoreCode == barcodeTagSettingsDto.StoreCode);

            if (existing == null)
            {
                var entity = _mapper.Map<BarcodeTagSettingsEntity>(barcodeTagSettingsDto);
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.BarcodeTagSettings.Add(entity);
            }
            else
            {
                existing.EnableTagging = barcodeTagSettingsDto.EnableTagging;
                existing.TagPrefix = barcodeTagSettingsDto.TagPrefix;
                existing.NextTagNumber = barcodeTagSettingsDto.NextTagNumber;
                existing.TagNumberPadding = barcodeTagSettingsDto.TagNumberPadding;
                existing.ResetTagNumberYearly = barcodeTagSettingsDto.ResetTagNumberYearly;
                existing.Notes = barcodeTagSettingsDto.Notes;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Barcode and tag settings saved successfully."
            });
        }

        public async Task<ActionReturnType> GetBarcodeTagSettings(string tenantName, string storeCode)
        {
            await EnsureBarcodeTagSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid barcode and tag settings request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var barcodeTagSettings = await _masterDbContext.BarcodeTagSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (barcodeTagSettings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Barcode and tag settings not found."
                });
            }

            var responseDto = _mapper.Map<BarcodeTagSettingsDto>(barcodeTagSettings);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        /// <summary>
        /// Replaces the full workflow status list for a store. The screen always posts the
        /// complete grid, so the simplest consistent model is delete-then-insert.
        /// </summary>
        public async Task<ActionReturnType> SaveWorkflowStatusSettings(WorkflowStatusSettingsDto workflowStatusSettingsDto)
        {
            await EnsureWorkflowStatusesTableExistsAsync();

            if (workflowStatusSettingsDto == null
                || string.IsNullOrWhiteSpace(workflowStatusSettingsDto.TenantName)
                || string.IsNullOrWhiteSpace(workflowStatusSettingsDto.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid workflow status settings request."
                });
            }

            var tenantName = workflowStatusSettingsDto.TenantName.Trim();
            var storeCode = workflowStatusSettingsDto.StoreCode.Trim();

            var incoming = (workflowStatusSettingsDto.Statuses ?? new List<WorkflowStatusDto>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StatusName))
                .ToList();

            if (incoming.Count == 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Please configure at least one workflow status."
                });
            }

            // Trim names and drop duplicates, keeping the first occurrence.
            var normalized = new List<WorkflowStatusDto>();
            foreach (var status in incoming)
            {
                var name = status.StatusName.Trim();
                if (name.Length > 64)
                {
                    name = name.Substring(0, 64);
                }

                if (normalized.Any(x => string.Equals(x.StatusName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                status.StatusName = name;
                status.ColorCode = string.IsNullOrWhiteSpace(status.ColorCode) ? "#6f7a86" : status.ColorCode.Trim();
                if (status.ColorCode.Length > 16)
                {
                    status.ColorCode = status.ColorCode.Substring(0, 16);
                }

                normalized.Add(status);
            }

            if (!normalized.Any(x => x.IsActive))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "At least one workflow status must be active."
                });
            }

            // Exactly one default is allowed, and it must be an active, non-final status.
            var defaultStatus = normalized.FirstOrDefault(x => x.IsDefault && x.IsActive)
                ?? normalized.First(x => x.IsActive);

            foreach (var status in normalized)
            {
                status.IsDefault = ReferenceEquals(status, defaultStatus);
            }

            var existing = await _masterDbContext.WorkflowStatuses
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .ToListAsync();

            if (existing.Count > 0)
            {
                _masterDbContext.WorkflowStatuses.RemoveRange(existing);
                await _masterDbContext.SaveChangesAsync();
            }

            var now = DateTime.UtcNow;
            var sortOrder = 1;

            foreach (var status in normalized.OrderBy(x => x.SortOrder))
            {
                _masterDbContext.WorkflowStatuses.Add(new WorkflowStatusEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    StatusName = status.StatusName,
                    SortOrder = sortOrder++,
                    ColorCode = status.ColorCode,
                    IsDefault = status.IsDefault,
                    IsFinal = status.IsFinal,
                    IsActive = status.IsActive,
                    CreatedDate = now,
                    ModifiedDate = now
                });
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Workflow status settings saved successfully."
            });
        }

        public async Task<ActionReturnType> GetWorkflowStatusSettings(string tenantName, string storeCode)
        {
            await EnsureWorkflowStatusesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid workflow status settings request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var statuses = await _masterDbContext.WorkflowStatuses
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();

            if (statuses.Count == 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Workflow status settings not found."
                });
            }

            var responseDto = new WorkflowStatusSettingsDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                Statuses = statuses.Select(x => _mapper.Map<WorkflowStatusDto>(x)).ToList()
            };

            return ActionSet.ActionReturnType(HttpStatusCode.OK, responseDto);
        }

        public async Task<ActionReturnType> SaveCustomerAdvance(CustomerAdvanceDto advanceDto)
        {
            await EnsureCustomerAdvancesTableExistsAsync();

            if (advanceDto == null
                || string.IsNullOrWhiteSpace(advanceDto.TenantName)
                || string.IsNullOrWhiteSpace(advanceDto.StoreCode)
                || string.IsNullOrWhiteSpace(advanceDto.CustCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid customer advance request."
                });
            }

            if (advanceDto.AdvanceAmount <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Advance amount should be greater than zero."
                });
            }

            advanceDto.TenantName = advanceDto.TenantName.Trim();
            advanceDto.StoreCode = advanceDto.StoreCode.Trim();
            advanceDto.CustCode = advanceDto.CustCode.Trim();
            advanceDto.TransactionType = string.IsNullOrWhiteSpace(advanceDto.TransactionType)
                ? "Credit"
                : advanceDto.TransactionType.Trim();
            advanceDto.Notes = string.IsNullOrWhiteSpace(advanceDto.Notes)
                ? null
                : advanceDto.Notes.Trim();

            if (!string.Equals(advanceDto.TransactionType, "Credit", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(advanceDto.TransactionType, "Debit", StringComparison.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Transaction type should be Credit or Debit."
                });
            }

            var customerExists = await _masterDbContext.Customers.AnyAsync(c =>
                c.CustCode == advanceDto.CustCode
                && c.TenantName == advanceDto.TenantName
                && c.StoreCode == advanceDto.StoreCode);

            if (!customerExists)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Customer not found for the selected store."
                });
            }

            var signedAmount = string.Equals(advanceDto.TransactionType, "Debit", StringComparison.OrdinalIgnoreCase)
                ? -Math.Abs(advanceDto.AdvanceAmount)
                : Math.Abs(advanceDto.AdvanceAmount);

            var currentBalance = await _masterDbContext.CustomerAdvances
                .Where(x => x.TenantName == advanceDto.TenantName
                            && x.StoreCode == advanceDto.StoreCode
                            && x.CustCode == advanceDto.CustCode)
                .SumAsync(x => (decimal?)x.AdvanceAmount) ?? 0;

            var nextBalance = currentBalance + signedAmount;
            if (nextBalance < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Debit amount cannot exceed current advance balance."
                });
            }

            var advanceEntity = new CustomerAdvanceEntity
            {
                TenantName = advanceDto.TenantName,
                StoreCode = advanceDto.StoreCode,
                CustCode = advanceDto.CustCode,
                AdvanceAmount = signedAmount,
                TransactionType = string.Equals(advanceDto.TransactionType, "Debit", StringComparison.OrdinalIgnoreCase) ? "Debit" : "Credit",
                Notes = advanceDto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _masterDbContext.CustomerAdvances.Add(advanceEntity);
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                CustCode = advanceDto.CustCode,
                Message = "Customer advance saved successfully."
            });
        }

        public async Task<ActionReturnType> GetCustomerAdvances(string tenantName, string storeCode, string custCode)
        {
            await EnsureCustomerAdvancesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(custCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid customer advance request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            custCode = custCode.Trim();

            var customerExists = await _masterDbContext.Customers.AnyAsync(c =>
                c.CustCode == custCode
                && c.TenantName == tenantName
                && c.StoreCode == storeCode);

            if (!customerExists)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Customer not found for the selected store."
                });
            }

            var advances = await _masterDbContext.CustomerAdvances
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.CustCode == custCode)
                .OrderByDescending(x => x.CreatedDate)
                .Select(x => new CustomerAdvanceDto
                {
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    CustCode = x.CustCode,
                    AdvanceAmount = Math.Abs(x.AdvanceAmount),
                    TransactionType = x.TransactionType,
                    Notes = x.Notes,
                    CreatedDate = x.CreatedDate
                })
                .ToListAsync();

            var balance = await _masterDbContext.CustomerAdvances
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.CustCode == custCode)
                .SumAsync(x => (decimal?)x.AdvanceAmount) ?? 0;

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerAdvanceResponseDto
            {
                StatusCode = "200",
                CustCode = custCode,
                Balance = balance,
                Transactions = advances
            });
        }

        public async Task<ActionReturnType> CreateLaundryOrder(LaundryOrderDto orderDto)
        {
            await EnsureLaundryOrdersTableExistsAsync();
            await EnsureCustomerAdvancesTableExistsAsync();
            await EnsureLaundryItemPricesTableExistsAsync();

            if (orderDto == null
                || string.IsNullOrWhiteSpace(orderDto.TenantName)
                || string.IsNullOrWhiteSpace(orderDto.StoreCode)
                || string.IsNullOrWhiteSpace(orderDto.CustCode)
                || string.IsNullOrWhiteSpace(orderDto.CustomerName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry order request."
                });
            }

            orderDto.TenantName = orderDto.TenantName.Trim();
            orderDto.StoreCode = orderDto.StoreCode.Trim();
            orderDto.CustCode = orderDto.CustCode.Trim();
            orderDto.CustomerName = orderDto.CustomerName.Trim();
            orderDto.ServiceType = string.IsNullOrWhiteSpace(orderDto.ServiceType) ? "Dry Clean" : orderDto.ServiceType.Trim();
            orderDto.OrderMode = string.IsNullOrWhiteSpace(orderDto.OrderMode) ? "pieces" : orderDto.OrderMode.Trim().ToLowerInvariant();
            orderDto.PaymentMode = string.IsNullOrWhiteSpace(orderDto.PaymentMode) ? "Cash" : orderDto.PaymentMode.Trim();
            orderDto.Notes = string.IsNullOrWhiteSpace(orderDto.Notes) ? null : orderDto.Notes.Trim();

            if (orderDto.OrderMode == "pieces")
            {
                var hasActivePricing = await _masterDbContext.LaundryItemPrices
                    .AsNoTracking()
                    .AnyAsync(x => x.TenantName == orderDto.TenantName
                                   && x.StoreCode == orderDto.StoreCode
                                   && x.IsActive);

                if (!hasActivePricing)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "No active laundry item pricing configured for this store. Please configure pricing in Admin Setup."
                    });
                }
            }

            if (orderDto.OrderMode != "pieces" && orderDto.OrderMode != "weight")
            {
                orderDto.OrderMode = "pieces";
            }

            if (orderDto.OrderMode == "weight")
            {
                if (orderDto.WeightInKg <= 0)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "Weight should be greater than zero for weight-based orders."
                    });
                }

                if (orderDto.RatePerKg <= 0)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "Rate per kg should be greater than zero for weight-based orders."
                    });
                }

                orderDto.OrderAmount = Math.Round(orderDto.WeightInKg * orderDto.RatePerKg, 2, MidpointRounding.AwayFromZero);
            }

            if (orderDto.OrderAmount <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Order amount should be greater than zero."
                });
            }

            if (orderDto.AdvanceUsed < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Advance used cannot be negative."
                });
            }

            var customer = await _masterDbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c =>
                c.CustCode == orderDto.CustCode
                && c.TenantName == orderDto.TenantName
                && c.StoreCode == orderDto.StoreCode);

            if (customer == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Customer not found for the selected store."
                });
            }

            var currentBalance = await _masterDbContext.CustomerAdvances
                .Where(x => x.TenantName == orderDto.TenantName
                            && x.StoreCode == orderDto.StoreCode
                            && x.CustCode == orderDto.CustCode)
                .SumAsync(x => (decimal?)x.AdvanceAmount) ?? 0;

            if (orderDto.AdvanceUsed > currentBalance)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Advance used cannot exceed current advance balance."
                });
            }

            await EnsureTaxInvoiceSettingsTableExistsAsync();

            var taxSettings = await _masterDbContext.TaxInvoiceSettings.FirstOrDefaultAsync(x =>
                x.TenantName == orderDto.TenantName && x.StoreCode == orderDto.StoreCode);

            var breakup = CalculateTaxBreakup(orderDto.OrderAmount, taxSettings);

            orderDto.AdvanceUsed = Math.Min(Math.Max(0, orderDto.AdvanceUsed), breakup.TotalAmount);
            var netAfterAdvance = Math.Max(0, breakup.TotalAmount - orderDto.AdvanceUsed);
            if (orderDto.PaidNow < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Paid amount cannot be negative."
                });
            }

            orderDto.PaidNow = Math.Min(Math.Round(orderDto.PaidNow, 2, MidpointRounding.AwayFromZero), netAfterAdvance);
            orderDto.PendingAmount = Math.Max(0, Math.Round(netAfterAdvance - orderDto.PaidNow, 2, MidpointRounding.AwayFromZero));
            orderDto.NetPayable = orderDto.PendingAmount;
            var orderNo = GenerateOrderNo();
            var invoiceNo = BuildInvoiceNo(taxSettings, orderNo);
            var defaultStatus = await GetDefaultWorkflowStatusAsync(orderDto.TenantName, orderDto.StoreCode);

            var orderEntity = new LaundryOrderEntity
            {
                TenantName = orderDto.TenantName,
                StoreCode = orderDto.StoreCode,
                CustCode = orderDto.CustCode,
                CustomerName = customer.CustomerName,
                OrderNo = orderNo,
                ServiceType = orderDto.ServiceType,
                OrderMode = orderDto.OrderMode,
                WeightInKg = orderDto.OrderMode == "weight" ? Math.Round(orderDto.WeightInKg, 3, MidpointRounding.AwayFromZero) : 0,
                RatePerKg = orderDto.OrderMode == "weight" ? Math.Round(orderDto.RatePerKg, 2, MidpointRounding.AwayFromZero) : 0,
                OrderAmount = orderDto.OrderAmount,
                SubTotal = breakup.SubTotal,
                TaxPercent = breakup.TaxPercent,
                TaxAmount = breakup.TaxAmount,
                CgstAmount = breakup.CgstAmount,
                SgstAmount = breakup.SgstAmount,
                TotalAmount = breakup.TotalAmount,
                InvoiceNo = invoiceNo,
                Status = defaultStatus,
                AdvanceUsed = orderDto.AdvanceUsed,
                PaidNow = orderDto.PaidNow,
                PendingAmount = orderDto.PendingAmount,
                NetPayable = orderDto.NetPayable,
                PaymentMode = orderDto.PaymentMode,
                Notes = orderDto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _masterDbContext.LaundryOrders.Add(orderEntity);

            if (orderDto.AdvanceUsed > 0)
            {
                var debitAdvance = new CustomerAdvanceEntity
                {
                    TenantName = orderDto.TenantName,
                    StoreCode = orderDto.StoreCode,
                    CustCode = orderDto.CustCode,
                    AdvanceAmount = -Math.Abs(orderDto.AdvanceUsed),
                    TransactionType = "Debit",
                    Notes = $"Advance used in order {orderNo}",
                    CreatedDate = DateTime.UtcNow
                };

                _masterDbContext.CustomerAdvances.Add(debitAdvance);
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderDto
            {
                TenantName = orderEntity.TenantName,
                StoreCode = orderEntity.StoreCode,
                CustCode = orderEntity.CustCode,
                CustomerName = orderEntity.CustomerName,
                OrderNo = orderEntity.OrderNo,
                ServiceType = orderEntity.ServiceType,
                OrderMode = orderEntity.OrderMode,
                WeightInKg = orderEntity.WeightInKg,
                RatePerKg = orderEntity.RatePerKg,
                OrderAmount = orderEntity.OrderAmount,
                SubTotal = orderEntity.SubTotal,
                TaxPercent = orderEntity.TaxPercent,
                TaxAmount = orderEntity.TaxAmount,
                CgstAmount = orderEntity.CgstAmount,
                SgstAmount = orderEntity.SgstAmount,
                TotalAmount = orderEntity.TotalAmount,
                InvoiceNo = orderEntity.InvoiceNo,
                Status = orderEntity.Status,
                AdvanceUsed = orderEntity.AdvanceUsed,
                PaidNow = orderEntity.PaidNow,
                PendingAmount = orderEntity.PendingAmount,
                NetPayable = orderEntity.NetPayable,
                PaymentMode = orderEntity.PaymentMode,
                Notes = orderEntity.Notes,
                CreatedDate = orderEntity.CreatedDate
            });
        }

        public async Task<ActionReturnType> SettleLaundryOrderPayment(string tenantName, string storeCode, string orderNo, decimal paidAmount, string paymentMode = null, string notes = null)
        {
            await EnsureLaundryOrdersTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(orderNo))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid settle payment request."
                });
            }

            if (paidAmount <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Paid amount should be greater than zero."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            orderNo = orderNo.Trim();
            paymentMode = string.IsNullOrWhiteSpace(paymentMode) ? null : paymentMode.Trim();
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            var order = await _masterDbContext.LaundryOrders.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.OrderNo == orderNo);

            if (order == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Order not found for this store."
                });
            }

            if (order.NetPayable <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Order has no pending amount to settle."
                });
            }

            var payableBefore = Math.Max(0, order.NetPayable);
            var applied = Math.Min(Math.Round(paidAmount, 2, MidpointRounding.AwayFromZero), payableBefore);
            order.PaidNow = Math.Round(order.PaidNow + applied, 2, MidpointRounding.AwayFromZero);
            order.NetPayable = Math.Round(Math.Max(0, payableBefore - applied), 2, MidpointRounding.AwayFromZero);
            order.PendingAmount = order.NetPayable;

            if (!string.IsNullOrWhiteSpace(paymentMode))
            {
                order.PaymentMode = paymentMode;
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                var notePrefix = $"Payment settled {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC: {applied:0.00}";
                order.Notes = string.IsNullOrWhiteSpace(order.Notes)
                    ? (notePrefix + " | " + notes)
                    : (order.Notes + " || " + notePrefix + " | " + notes);
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderDto
            {
                TenantName = order.TenantName,
                StoreCode = order.StoreCode,
                CustCode = order.CustCode,
                CustomerName = order.CustomerName,
                OrderNo = order.OrderNo,
                ServiceType = order.ServiceType,
                OrderMode = order.OrderMode,
                WeightInKg = order.WeightInKg,
                RatePerKg = order.RatePerKg,
                OrderAmount = order.OrderAmount,
                SubTotal = order.SubTotal,
                TaxPercent = order.TaxPercent,
                TaxAmount = order.TaxAmount,
                CgstAmount = order.CgstAmount,
                SgstAmount = order.SgstAmount,
                TotalAmount = order.TotalAmount,
                InvoiceNo = order.InvoiceNo,
                Status = order.Status,
                AdvanceUsed = order.AdvanceUsed,
                PaidNow = order.PaidNow,
                PendingAmount = order.PendingAmount,
                NetPayable = order.NetPayable,
                PaymentMode = order.PaymentMode,
                Notes = order.Notes,
                CreatedDate = order.CreatedDate
            });
        }

        /// <summary>
        /// Resolves the status new orders should start in for the supplied store.
        /// Falls back to the lowest ordered active status, then to "Received".
        /// </summary>
        private async Task<string> GetDefaultWorkflowStatusAsync(string tenantName, string storeCode)
        {
            await EnsureWorkflowStatusesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return "Received";
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var statuses = await _masterDbContext.WorkflowStatuses
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();

            var defaultStatus = statuses.FirstOrDefault(x => x.IsDefault) ?? statuses.FirstOrDefault();
            return defaultStatus?.StatusName ?? "Received";
        }

        public async Task<ActionReturnType> GetLaundryOrders(string tenantName, string storeCode, string custCode)
        {
            await EnsureLaundryOrdersTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(custCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry order request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            custCode = custCode.Trim();

            var orders = await _masterDbContext.LaundryOrders
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.CustCode == custCode)
                .OrderByDescending(x => x.CreatedDate)
                .Select(x => new LaundryOrderDto
                {
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    CustCode = x.CustCode,
                    CustomerName = x.CustomerName,
                    OrderNo = x.OrderNo,
                    InvoiceNo = x.InvoiceNo,
                    ServiceType = x.ServiceType,
                    OrderMode = x.OrderMode,
                    WeightInKg = x.WeightInKg,
                    RatePerKg = x.RatePerKg,
                    OrderAmount = x.OrderAmount,
                    SubTotal = x.SubTotal,
                    TaxPercent = x.TaxPercent,
                    TaxAmount = x.TaxAmount,
                    CgstAmount = x.CgstAmount,
                    SgstAmount = x.SgstAmount,
                    TotalAmount = x.TotalAmount,
                    Status = x.Status,
                    AdvanceUsed = x.AdvanceUsed,
                    PaidNow = x.PaidNow,
                    PendingAmount = x.PendingAmount,
                    NetPayable = x.NetPayable,
                    PaymentMode = x.PaymentMode,
                    Notes = x.Notes,
                    CreatedDate = x.CreatedDate
                })
                .ToListAsync();

            foreach (var order in orders)
            {
                if (TryNormalizeWorkflowLifecycleStatus(order.Status, out var normalized))
                {
                    order.Status = normalized;
                }

                if (string.Equals(order.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase))
                {
                    order.TotalPieces = ExtractTotalPieces(order.Notes);
                }
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderResponseDto
            {
                StatusCode = "200",
                CustCode = custCode,
                Orders = orders
            });
        }

        public async Task<ActionReturnType> GetStoreLaundryOrders(string tenantName, string storeCode, string status = null, string searchText = null)
        {
            await EnsureLaundryOrdersTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid workflow order request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            status = status?.Trim();
            searchText = searchText?.Trim();

            var query = _masterDbContext.LaundryOrders
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (!string.IsNullOrWhiteSpace(status)
                && TryNormalizeWorkflowLifecycleStatus(status, out var normalizedFilter))
            {
                switch (normalizedFilter)
                {
                    case "Received":
                        query = query.Where(x => x.Status == "Received");
                        break;
                    case "Processing":
                        query = query.Where(x => x.Status == "Processing" || x.Status == "In Process");
                        break;
                    case "Ready":
                        query = query.Where(x => x.Status == "Ready" || x.Status == "Ready for Delivery");
                        break;
                    case "Delivered":
                        query = query.Where(x => x.Status == "Delivered");
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var searchLower = searchText.ToLowerInvariant();
                query = query.Where(x =>
                    (!string.IsNullOrEmpty(x.OrderNo) && x.OrderNo.ToLower().Contains(searchLower))
                    || (!string.IsNullOrEmpty(x.InvoiceNo) && x.InvoiceNo.ToLower().Contains(searchLower))
                    || (!string.IsNullOrEmpty(x.CustomerName) && x.CustomerName.ToLower().Contains(searchLower))
                    || (!string.IsNullOrEmpty(x.CustCode) && x.CustCode.ToLower().Contains(searchLower)));
            }

            var orders = await query
                .OrderByDescending(x => x.CreatedDate)
                .Take(500)
                .Select(x => new LaundryOrderDto
                {
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    CustCode = x.CustCode,
                    CustomerName = x.CustomerName,
                    OrderNo = x.OrderNo,
                    InvoiceNo = x.InvoiceNo,
                    ServiceType = x.ServiceType,
                    OrderMode = x.OrderMode,
                    WeightInKg = x.WeightInKg,
                    RatePerKg = x.RatePerKg,
                    OrderAmount = x.OrderAmount,
                    SubTotal = x.SubTotal,
                    TaxPercent = x.TaxPercent,
                    TaxAmount = x.TaxAmount,
                    CgstAmount = x.CgstAmount,
                    SgstAmount = x.SgstAmount,
                    TotalAmount = x.TotalAmount,
                    Status = x.Status,
                    AdvanceUsed = x.AdvanceUsed,
                    PaidNow = x.PaidNow,
                    PendingAmount = x.PendingAmount,
                    NetPayable = x.NetPayable,
                    PaymentMode = x.PaymentMode,
                    Notes = x.Notes,
                    CreatedDate = x.CreatedDate
                    
                })
                .ToListAsync();

            foreach (var order in orders)
            {
                if (TryNormalizeWorkflowLifecycleStatus(order.Status, out var normalized))
                {
                    order.Status = normalized;
                }

                if (string.Equals(order.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase))
                {
                    order.TotalPieces = ExtractTotalPieces(order.Notes);
                }
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderResponseDto
            {
                StatusCode = "200",
                Orders = orders
            });
        }

        private static int ExtractTotalPieces(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return 0;
            }

            var match = Regex.Match(
                notes,
                @"\bx\s*(\d+)\b",
                RegexOptions.IgnoreCase);

            return match.Success && int.TryParse(match.Groups[1].Value, out var quantity)
                ? quantity
                : 0;
        }

        public async Task<ActionReturnType> UpdateLaundryOrderStatus(string tenantName, string storeCode, string orderNo, string status)
        {
            await EnsureLaundryOrdersTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(orderNo)
                || string.IsNullOrWhiteSpace(status))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Tenant, store, order number and status are required."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            orderNo = orderNo.Trim();
            status = status.Trim();

            if (!TryNormalizeWorkflowLifecycleStatus(status, out var targetStatus))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid status. Allowed values: Received, Processing, Ready, Delivered."
                });
            }

            var order = await _masterDbContext.LaundryOrders.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.OrderNo == orderNo);

            if (order == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Order not found for this store."
                });
            }

            if (!TryNormalizeWorkflowLifecycleStatus(order.Status, out var currentStatus))
            {
                currentStatus = "Received";
            }

            if (string.Equals(currentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "Order status is already up to date."
                });
            }

            if (!CanMoveWorkflowStatus(currentStatus, targetStatus))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = $"Invalid transition from '{currentStatus}' to '{targetStatus}'."
                });
            }

            order.Status = targetStatus;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = $"Order status moved to {targetStatus}."
            });
        }

        private static bool TryNormalizeWorkflowLifecycleStatus(string status, out string normalizedStatus)
        {
            normalizedStatus = null;
            var value = status?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, "Received", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "Received";
                return true;
            }

            if (string.Equals(value, "Processing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "In Process", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "Processing";
                return true;
            }

            if (string.Equals(value, "Ready", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Ready for Delivery", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "Ready";
                return true;
            }

            if (string.Equals(value, "Delivered", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "Delivered";
                return true;
            }

            return false;
        }

        private static bool CanMoveWorkflowStatus(string currentStatus, string targetStatus)
        {
            if (string.Equals(currentStatus, "Received", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(targetStatus, "Processing", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(currentStatus, "Processing", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(targetStatus, "Ready", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(currentStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(targetStatus, "Delivered", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public async Task<ActionReturnType> GetLaundryDeliveryStatus(string tenantName, string storeCode)
        {
            await EnsureLaundryOrdersTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid delivery status request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var baseQuery = _masterDbContext.LaundryOrders
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            var readyForDeliveryCount = await baseQuery
                .CountAsync(x => x.NetPayable <= 0);

            var pendingDeliveryCount = await baseQuery
                .CountAsync(x => x.NetPayable > 0);

            var totalOrders = readyForDeliveryCount + pendingDeliveryCount;

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryDeliveryStatusResponseDto
            {
                StatusCode = "200",
                TenantName = tenantName,
                StoreCode = storeCode,
                ReadyForDeliveryCount = readyForDeliveryCount,
                PendingDeliveryCount = pendingDeliveryCount,
                TotalOrdersConsidered = totalOrders
            });
        }

        public async Task<ActionReturnType> SaveLaundryItemPrice(LaundryItemPriceDto itemPriceDto)
        {
            await EnsureLaundryItemPricesTableExistsAsync();

            if (itemPriceDto == null
                || string.IsNullOrWhiteSpace(itemPriceDto.TenantName)
                || string.IsNullOrWhiteSpace(itemPriceDto.StoreCode)
                || string.IsNullOrWhiteSpace(itemPriceDto.ServiceType)
                || string.IsNullOrWhiteSpace(itemPriceDto.Category)
                || string.IsNullOrWhiteSpace(itemPriceDto.ItemName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry item price request."
                });
            }

            if (itemPriceDto.UnitPrice < 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Item price cannot be negative."
                });
            }

            itemPriceDto.TenantName = itemPriceDto.TenantName.Trim();
            itemPriceDto.StoreCode = itemPriceDto.StoreCode.Trim();
            itemPriceDto.ServiceType = itemPriceDto.ServiceType.Trim();
            itemPriceDto.Category = itemPriceDto.Category.Trim();
            itemPriceDto.ItemName = itemPriceDto.ItemName.Trim();

            var existing = await _masterDbContext.LaundryItemPrices.FirstOrDefaultAsync(x =>
                x.TenantName == itemPriceDto.TenantName
                && x.StoreCode == itemPriceDto.StoreCode
                && x.ServiceType == itemPriceDto.ServiceType
                && x.Category == itemPriceDto.Category
                && x.ItemName == itemPriceDto.ItemName);

            if (existing == null)
            {
                var entity = _mapper.Map<LaundryItemPriceEntity>(itemPriceDto);
                entity.IsActive = true;
                entity.CreatedDate = DateTime.UtcNow;
                entity.ModifiedDate = DateTime.UtcNow;
                _masterDbContext.LaundryItemPrices.Add(entity);
            }
            else
            {
                existing.UnitPrice = itemPriceDto.UnitPrice;
                existing.IsActive = itemPriceDto.IsActive;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Laundry item price saved successfully."
            });
        }

        public async Task<ActionReturnType> DeactivateLaundryItemPrice(string tenantName, string storeCode, string serviceType, string category, string itemName)
        {
            await EnsureLaundryItemPricesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry item remove request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            serviceType = serviceType.Trim();
            category = category.Trim();
            itemName = itemName.Trim();

            var existing = await _masterDbContext.LaundryItemPrices.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.ServiceType == serviceType
                && x.Category == category
                && x.ItemName == itemName);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Laundry item not found."
                });
            }

            if (!existing.IsActive)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "Laundry item is already removed."
                });
            }

            existing.IsActive = false;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Laundry item removed successfully."
            });
        }

        public async Task<ActionReturnType> ReactivateLaundryItemPrice(string tenantName, string storeCode, string serviceType, string category, string itemName)
        {
            await EnsureLaundryItemPricesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry item restore request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            serviceType = serviceType.Trim();
            category = category.Trim();
            itemName = itemName.Trim();

            var existing = await _masterDbContext.LaundryItemPrices.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.ServiceType == serviceType
                && x.Category == category
                && x.ItemName == itemName);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Laundry item not found."
                });
            }

            if (existing.IsActive)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "Laundry item is already active."
                });
            }

            existing.IsActive = true;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Laundry item restored successfully."
            });
        }

        public async Task<ActionReturnType> GetLaundryItemPrices(string tenantName, string storeCode, bool includeInactive = false)
        {
            await EnsureLaundryItemPricesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry item price request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var hasAnyConfiguredItems = await _masterDbContext.LaundryItemPrices
                .AsNoTracking()
                .AnyAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (!hasAnyConfiguredItems)
            {
                var now = DateTime.UtcNow;
                var defaults = BuildDefaultLaundryItemPrices(tenantName, storeCode);
                defaults.ForEach(x =>
                {
                    x.CreatedDate = now;
                    x.ModifiedDate = now;
                    x.IsActive = true;
                });

                _masterDbContext.LaundryItemPrices.AddRange(defaults);
                await _masterDbContext.SaveChangesAsync();
            }

            var query = _masterDbContext.LaundryItemPrices
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode);

            if (!includeInactive)
            {
                query = query.Where(x => x.IsActive);
            }

            var prices = await query
                .OrderBy(x => x.ServiceType)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => new LaundryItemPriceDto
                {
                    Id = x.Id,
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    ServiceType = x.ServiceType,
                    Category = x.Category,
                    ItemName = x.ItemName,
                    UnitPrice = x.UnitPrice,
                    IsActive = x.IsActive,
                    CreatedDate = x.CreatedDate,
                    ModifiedDate = x.ModifiedDate
                })
                .ToListAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, prices);
        }

        public async Task<ActionReturnType> ImportDefaultLaundryItemPrices(string tenantName, string storeCode, bool overwriteExisting)
        {
            await EnsureLaundryItemPricesTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid laundry item price request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var defaultItems = BuildDefaultLaundryItemPrices(tenantName, storeCode);
            var existingItems = await _masterDbContext.LaundryItemPrices
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .ToListAsync();

            var existingMap = existingItems.ToDictionary(
                k => $"{k.ServiceType}|{k.Category}|{k.ItemName}",
                v => v,
                StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var importedCount = 0;

            foreach (var item in defaultItems)
            {
                var key = $"{item.ServiceType}|{item.Category}|{item.ItemName}";
                if (existingMap.TryGetValue(key, out var existing))
                {
                    if (overwriteExisting)
                    {
                        existing.UnitPrice = item.UnitPrice;
                        existing.IsActive = true;
                        existing.ModifiedDate = now;
                        importedCount++;
                    }

                    continue;
                }

                item.CreatedDate = now;
                item.ModifiedDate = now;
                _masterDbContext.LaundryItemPrices.Add(item);
                importedCount++;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = overwriteExisting
                    ? $"Default laundry item pricing imported successfully. Items inserted/updated: {importedCount}."
                    : $"Default laundry item pricing imported successfully. New items inserted: {importedCount}."
            });
        }

        private static string NormalizeMasterType(string masterType)
        {
            return string.Equals(masterType?.Trim(), "Category", StringComparison.OrdinalIgnoreCase)
                ? "Category"
                : "Service";
        }

        public async Task<ActionReturnType> SaveStoreServiceMaster(StoreServiceMasterDto masterDto)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (masterDto == null
                || string.IsNullOrWhiteSpace(masterDto.TenantName)
                || string.IsNullOrWhiteSpace(masterDto.StoreCode)
                || string.IsNullOrWhiteSpace(masterDto.Name))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid service master request."
                });
            }

            var tenantName = masterDto.TenantName.Trim();
            var storeCode = masterDto.StoreCode.Trim();
            var masterType = NormalizeMasterType(masterDto.MasterType);
            var name = masterDto.Name.Trim();
            var description = string.IsNullOrWhiteSpace(masterDto.Description) ? null : masterDto.Description.Trim();

            var existing = await _masterDbContext.StoreServiceMasters.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.MasterType == masterType
                && x.Name == name);

            if (existing == null)
            {
                _masterDbContext.StoreServiceMasters.Add(new StoreServiceMasterEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    MasterType = masterType,
                    Name = name,
                    Description = description,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                });
            }
            else
            {
                existing.Description = description;
                existing.IsActive = true;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = $"{masterType} saved successfully."
            });
        }

        public async Task<ActionReturnType> SetStoreServiceMasterStatus(string tenantName, string storeCode, string masterType, string name, bool isActive)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(name))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid service master request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            var normalizedType = NormalizeMasterType(masterType);
            name = name.Trim();

            var existing = await _masterDbContext.StoreServiceMasters.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.MasterType == normalizedType
                && x.Name == name);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = $"{normalizedType} not found."
                });
            }

            existing.IsActive = isActive;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = isActive
                    ? $"{normalizedType} restored successfully."
                    : $"{normalizedType} removed successfully."
            });
        }

        public async Task<ActionReturnType> GetStoreServiceMasters(string tenantName, string storeCode, bool includeInactive = false)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid service master request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var query = _masterDbContext.StoreServiceMasters
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (!includeInactive)
            {
                query = query.Where(x => x.IsActive);
            }

            var masters = await query
                .OrderBy(x => x.MasterType)
                .ThenBy(x => x.Name)
                .Select(x => new StoreServiceMasterDto
                {
                    Id = x.Id,
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    MasterType = x.MasterType,
                    Name = x.Name,
                    Description = x.Description,
                    IsActive = x.IsActive,
                    CreatedDate = x.CreatedDate,
                    ModifiedDate = x.ModifiedDate
                })
                .ToListAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, masters);
        }

        public async Task<ActionReturnType> SaveStoreItemMaster(StoreItemMasterDto itemDto)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (itemDto == null
                || string.IsNullOrWhiteSpace(itemDto.TenantName)
                || string.IsNullOrWhiteSpace(itemDto.StoreCode)
                || string.IsNullOrWhiteSpace(itemDto.ServiceType)
                || string.IsNullOrWhiteSpace(itemDto.Category)
                || string.IsNullOrWhiteSpace(itemDto.ItemName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid item master request."
                });
            }

            var tenantName = itemDto.TenantName.Trim();
            var storeCode = itemDto.StoreCode.Trim();
            var serviceType = itemDto.ServiceType.Trim();
            var category = itemDto.Category.Trim();
            var itemName = itemDto.ItemName.Trim();
            var description = string.IsNullOrWhiteSpace(itemDto.Description) ? null : itemDto.Description.Trim();

            var serviceExists = await _masterDbContext.StoreServiceMasters.AnyAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.MasterType == "Service"
                && x.Name == serviceType
                && x.IsActive);

            var categoryExists = await _masterDbContext.StoreServiceMasters.AnyAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.MasterType == "Category"
                && x.Name == category
                && x.IsActive);

            if (!serviceExists || !categoryExists)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Please add the service type and category to the master for this store before adding items."
                });
            }

            var existing = await _masterDbContext.StoreItemMasters.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.ServiceType == serviceType
                && x.Category == category
                && x.ItemName == itemName);

            if (existing == null)
            {
                _masterDbContext.StoreItemMasters.Add(new StoreItemMasterEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = serviceType,
                    Category = category,
                    ItemName = itemName,
                    Description = description,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                });
            }
            else
            {
                existing.Description = description;
                existing.IsActive = true;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Item master saved successfully."
            });
        }

        public async Task<ActionReturnType> SetStoreItemMasterStatus(string tenantName, string storeCode, string serviceType, string category, string itemName, bool isActive)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(serviceType)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(itemName))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid item master request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            serviceType = serviceType.Trim();
            category = category.Trim();
            itemName = itemName.Trim();

            var existing = await _masterDbContext.StoreItemMasters.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.ServiceType == serviceType
                && x.Category == category
                && x.ItemName == itemName);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Item master entry not found."
                });
            }

            existing.IsActive = isActive;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = isActive
                    ? "Item master entry restored successfully."
                    : "Item master entry removed successfully."
            });
        }

        public async Task<ActionReturnType> GetStoreItemMasters(string tenantName, string storeCode, bool includeInactive = false)
        {
            await EnsureStoreMasterTablesExistAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid item master request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var query = _masterDbContext.StoreItemMasters
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (!includeInactive)
            {
                query = query.Where(x => x.IsActive);
            }

            var items = await query
                .OrderBy(x => x.ServiceType)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => new StoreItemMasterDto
                {
                    Id = x.Id,
                    TenantName = x.TenantName,
                    StoreCode = x.StoreCode,
                    ServiceType = x.ServiceType,
                    Category = x.Category,
                    ItemName = x.ItemName,
                    Description = x.Description,
                    IsActive = x.IsActive,
                    CreatedDate = x.CreatedDate,
                    ModifiedDate = x.ModifiedDate
                })
                .ToListAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, items);
        }

        private async Task EnsureCustomerPreferencesTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[CustomerPreferences]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CustomerPreferences](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [EnableSmsNotifications] [bit] NOT NULL,
        [EnableEmailNotifications] [bit] NOT NULL,
        [AutoGenerateCustomerCode] [bit] NOT NULL,
        [RequirePhoneNumber] [bit] NOT NULL,
        [RequireEmail] [bit] NOT NULL,
        [AllowDuplicatePhoneNumber] [bit] NOT NULL,
        [DefaultServiceType] [nvarchar](64) NULL,
        [DefaultPaymentMode] [nvarchar](64) NULL,
        [DefaultStarchLevel] [nvarchar](32) NULL,
        [PickupReminderHours] [int] NOT NULL,
        [LoyaltyPointsPerOrder] [int] NOT NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_CustomerPreferences] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CustomerPreferences_TenantName_StoreCode'
      AND object_id = OBJECT_ID(N'[dbo].[CustomerPreferences]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_CustomerPreferences_TenantName_StoreCode]
    ON [dbo].[CustomerPreferences] ([TenantName], [StoreCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsurePricingRulesTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[PricingRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PricingRules](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [EnableExpressSurcharge] [bit] NOT NULL,
        [ExpressSurchargePercent] [decimal](5,2) NOT NULL,
        [EnableMinimumOrder] [bit] NOT NULL,
        [MinimumOrderAmount] [decimal](18,2) NOT NULL,
        [StandardTurnaroundHours] [int] NOT NULL,
        [ExpressTurnaroundHours] [int] NOT NULL,
        [RoundOffInvoiceTotal] [bit] NOT NULL,
        [Notes] [nvarchar](300) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_PricingRules] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PricingRules_TenantName_StoreCode'
      AND object_id = OBJECT_ID(N'[dbo].[PricingRules]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_PricingRules_TenantName_StoreCode]
    ON [dbo].[PricingRules] ([TenantName], [StoreCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsurePaymentSettingsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[PaymentSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PaymentSettings](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [EnableCash] [bit] NOT NULL,
        [EnableUpi] [bit] NOT NULL,
        [EnableCard] [bit] NOT NULL,
        [EnableWallet] [bit] NOT NULL,
        [DefaultPaymentMode] [nvarchar](32) NULL,
        [AllowPartialPayment] [bit] NOT NULL,
        [AllowCredit] [bit] NOT NULL,
        [CreditLimitAmount] [decimal](18,2) NOT NULL,
        [RoundOffPayableAmount] [bit] NOT NULL,
        [Notes] [nvarchar](300) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_PaymentSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PaymentSettings_TenantName_StoreCode'
      AND object_id = OBJECT_ID(N'[dbo].[PaymentSettings]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_PaymentSettings_TenantName_StoreCode]
    ON [dbo].[PaymentSettings] ([TenantName], [StoreCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureTaxInvoiceSettingsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[TaxInvoiceSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TaxInvoiceSettings](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [EnableTax] [bit] NOT NULL,
        [GstPercent] [decimal](5,2) NOT NULL,
        [PricesIncludeTax] [bit] NOT NULL,
        [InvoicePrefix] [nvarchar](16) NULL,
        [NextInvoiceNumber] [int] NOT NULL,
        [InvoiceNumberPadding] [int] NOT NULL,
        [ResetInvoiceNumberYearly] [bit] NOT NULL,
        [Notes] [nvarchar](300) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_TaxInvoiceSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceSettings_TenantName_StoreCode'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_TaxInvoiceSettings_TenantName_StoreCode]
    ON [dbo].[TaxInvoiceSettings] ([TenantName], [StoreCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureLaundryOrderItemsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[LaundryOrderItems]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LaundryOrderItems](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [OrderNo] [nvarchar](64) NOT NULL,
        [ServiceType] [nvarchar](64) NULL,
        [Category] [nvarchar](64) NULL,
        [ItemName] [nvarchar](128) NULL,
        [UnitPrice] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrderItems_UnitPrice] DEFAULT((0)),
        [PieceNo] [int] NOT NULL CONSTRAINT [DF_LaundryOrderItems_PieceNo] DEFAULT((1)),
        [TagNo] [nvarchar](32) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_LaundryOrderItems] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LaundryOrderItems_OrderNo'
      AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LaundryOrderItems_OrderNo]
    ON [dbo].[LaundryOrderItems] ([OrderNo]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
      AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo]
    ON [dbo].[LaundryOrderItems] ([TenantName], [StoreCode], [TagNo]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureBarcodeTagSettingsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[BarcodeTagSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BarcodeTagSettings](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [EnableTagging] [bit] NOT NULL,
        [TagPrefix] [nvarchar](16) NULL,
        [NextTagNumber] [int] NOT NULL,
        [TagNumberPadding] [int] NOT NULL,
        [ResetTagNumberYearly] [bit] NOT NULL,
        [Notes] [nvarchar](300) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_BarcodeTagSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_BarcodeTagSettings_TenantName_StoreCode'
      AND object_id = OBJECT_ID(N'[dbo].[BarcodeTagSettings]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_BarcodeTagSettings_TenantName_StoreCode]
    ON [dbo].[BarcodeTagSettings] ([TenantName], [StoreCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureWorkflowStatusesTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[WorkflowStatuses]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowStatuses](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [StatusName] [nvarchar](64) NOT NULL,
        [SortOrder] [int] NOT NULL,
        [ColorCode] [nvarchar](16) NULL,
        [IsDefault] [bit] NOT NULL,
        [IsFinal] [bit] NOT NULL,
        [IsActive] [bit] NOT NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_WorkflowStatuses] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_WorkflowStatuses_TenantName_StoreCode_StatusName'
      AND object_id = OBJECT_ID(N'[dbo].[WorkflowStatuses]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_WorkflowStatuses_TenantName_StoreCode_StatusName]
    ON [dbo].[WorkflowStatuses] ([TenantName], [StoreCode], [StatusName]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureLaundryItemPricesTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[LaundryItemPrices]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LaundryItemPrices](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [ServiceType] [nvarchar](64) NOT NULL,
        [Category] [nvarchar](64) NOT NULL,
        [ItemName] [nvarchar](128) NOT NULL,
        [UnitPrice] [decimal](18,2) NOT NULL,
        [IsActive] [bit] NOT NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_LaundryItemPrices] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

-- Older databases created this table before pricing became store specific.
-- Add the column so every row can be attributed to exactly one store.
IF COL_LENGTH(N'[dbo].[LaundryItemPrices]', N'StoreCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[LaundryItemPrices] ADD [StoreCode] [nvarchar](64) NULL;
END;

-- Legacy tenant-wide rows have no store and would otherwise be invisible to every
-- store. Attach them to the tenant's first store so they stay editable, then drop
-- any that cannot be attributed.
EXEC(N'
UPDATE p
SET p.StoreCode = s.StoreCode
FROM [dbo].[LaundryItemPrices] p
CROSS APPLY (
    SELECT TOP (1) q.StoreCode
    FROM [dbo].[LaundryItemPrices] q
    WHERE q.TenantName = p.TenantName
      AND q.StoreCode IS NOT NULL
      AND LTRIM(RTRIM(q.StoreCode)) <> N''''
    ORDER BY q.StoreCode
) s
WHERE p.StoreCode IS NULL OR LTRIM(RTRIM(p.StoreCode)) = N'''';

DELETE FROM [dbo].[LaundryItemPrices]
WHERE StoreCode IS NULL OR LTRIM(RTRIM(StoreCode)) = N'''';');

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[LaundryItemPrices]')
      AND name = N'StoreCode'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE [dbo].[LaundryItemPrices] ALTER COLUMN [StoreCode] [nvarchar](64) NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LaundryItemPrices_Tenant_Store_Service_Category_Item'
      AND object_id = OBJECT_ID(N'[dbo].[LaundryItemPrices]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_LaundryItemPrices_Tenant_Store_Service_Category_Item]
    ON [dbo].[LaundryItemPrices] ([TenantName], [StoreCode], [ServiceType], [Category], [ItemName]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureStoreMasterTablesExistAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[StoreServiceMaster]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StoreServiceMaster](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [MasterType] [nvarchar](32) NOT NULL,
        [Name] [nvarchar](128) NOT NULL,
        [Description] [nvarchar](256) NULL,
        [IsActive] [bit] NOT NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_StoreServiceMaster] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_StoreServiceMaster_Tenant_Store_Type_Name'
      AND object_id = OBJECT_ID(N'[dbo].[StoreServiceMaster]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_StoreServiceMaster_Tenant_Store_Type_Name]
    ON [dbo].[StoreServiceMaster] ([TenantName], [StoreCode], [MasterType], [Name]);
END;

IF OBJECT_ID(N'[dbo].[StoreItemMaster]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[StoreItemMaster](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [ServiceType] [nvarchar](64) NOT NULL,
        [Category] [nvarchar](64) NOT NULL,
        [ItemName] [nvarchar](128) NOT NULL,
        [Description] [nvarchar](256) NULL,
        [IsActive] [bit] NOT NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_StoreItemMaster] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_StoreItemMaster_Tenant_Store_Service_Category_Item'
      AND object_id = OBJECT_ID(N'[dbo].[StoreItemMaster]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_StoreItemMaster_Tenant_Store_Service_Category_Item]
    ON [dbo].[StoreItemMaster] ([TenantName], [StoreCode], [ServiceType], [Category], [ItemName]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private static (decimal SubTotal, decimal TaxPercent, decimal TaxAmount, decimal CgstAmount, decimal SgstAmount, decimal TotalAmount)
            CalculateTaxBreakup(decimal orderAmount, TaxInvoiceSettingsEntity taxSettings)
        {
            var gross = Math.Round(Math.Max(0, orderAmount), 2, MidpointRounding.AwayFromZero);

            if (taxSettings == null || !taxSettings.EnableTax || taxSettings.GstPercent <= 0)
            {
                return (gross, 0m, 0m, 0m, 0m, gross);
            }

            var percent = taxSettings.GstPercent;
            var rate = percent / 100m;

            decimal subTotal;
            decimal totalAmount;

            if (taxSettings.PricesIncludeTax)
            {
                totalAmount = gross;
                subTotal = Math.Round(gross / (1m + rate), 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                subTotal = gross;
                totalAmount = Math.Round(gross * (1m + rate), 2, MidpointRounding.AwayFromZero);
            }

            var taxAmount = Math.Round(totalAmount - subTotal, 2, MidpointRounding.AwayFromZero);
            var cgst = Math.Round(taxAmount / 2m, 2, MidpointRounding.AwayFromZero);
            var sgst = Math.Round(taxAmount - cgst, 2, MidpointRounding.AwayFromZero);

            return (subTotal, percent, taxAmount, cgst, sgst, totalAmount);
        }

        private static string BuildInvoiceNo(TaxInvoiceSettingsEntity taxSettings, string fallbackOrderNo)
        {
            if (taxSettings == null)
            {
                return fallbackOrderNo;
            }

            var nextNumber = taxSettings.NextInvoiceNumber < 1 ? 1 : taxSettings.NextInvoiceNumber;

            if (taxSettings.ResetInvoiceNumberYearly && taxSettings.ModifiedDate.Year < DateTime.UtcNow.Year)
            {
                nextNumber = 1;
            }

            var padding = taxSettings.InvoiceNumberPadding < 1 || taxSettings.InvoiceNumberPadding > 12
                ? 4
                : taxSettings.InvoiceNumberPadding;

            var prefix = string.IsNullOrWhiteSpace(taxSettings.InvoicePrefix)
                ? BuildStoreInvoicePrefix(taxSettings.StoreCode)
                : taxSettings.InvoicePrefix.Trim();

            // The year segment makes it obvious which year an invoice belongs to, e.g. MYCOM-YLJ-2026-0001.
            if (!prefix.EndsWith("-", StringComparison.Ordinal))
            {
                prefix += "-";
            }

            var yearSegment = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture) + "-";

            var invoiceNo = prefix + yearSegment + nextNumber.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0');

            taxSettings.NextInvoiceNumber = nextNumber + 1;
            taxSettings.ModifiedDate = DateTime.UtcNow;

            return invoiceNo;
        }

        /// <summary>
        /// Builds a readable invoice prefix for the store when no prefix was configured, for example "YLJ-".
        /// </summary>
        private static string BuildStoreInvoicePrefix(string storeCode)
        {
            var cleaned = new string((storeCode ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();

            if (cleaned.Length == 0)
            {
                return "INV-";
            }

            if (cleaned.Length > 6)
            {
                cleaned = cleaned.Substring(0, 6);
            }

            return cleaned + "-";
        }

        private async Task EnsureLaundryOrdersTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LaundryOrders](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [CustCode] [nvarchar](64) NOT NULL,
        [CustomerName] [nvarchar](256) NOT NULL,
        [OrderNo] [nvarchar](64) NOT NULL,
        [ServiceType] [nvarchar](64) NOT NULL,
        [OrderMode] [nvarchar](16) NOT NULL,
        [WeightInKg] [decimal](18,3) NOT NULL CONSTRAINT [DF_LaundryOrders_WeightInKg] DEFAULT((0)),
        [RatePerKg] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_RatePerKg] DEFAULT((0)),
        [OrderAmount] [decimal](18,2) NOT NULL,
        [SubTotal] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_SubTotal_New] DEFAULT((0)),
        [TaxPercent] [decimal](5,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TaxPercent_New] DEFAULT((0)),
        [TaxAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TaxAmount_New] DEFAULT((0)),
        [CgstAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_CgstAmount_New] DEFAULT((0)),
        [SgstAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_SgstAmount_New] DEFAULT((0)),
        [TotalAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TotalAmount_New] DEFAULT((0)),
        [InvoiceNo] [nvarchar](32) NULL,
        [Status] [nvarchar](64) NULL,
        [AdvanceUsed] [decimal](18,2) NOT NULL,
        [PaidNow] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_PaidNow] DEFAULT((0)),
        [PendingAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_PendingAmount] DEFAULT((0)),
        [NetPayable] [decimal](18,2) NOT NULL,
        [PaymentMode] [nvarchar](64) NOT NULL,
        [Notes] [nvarchar](500) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_LaundryOrders] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LaundryOrders_OrderNo'
      AND object_id = OBJECT_ID(N'[dbo].[LaundryOrders]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_LaundryOrders_OrderNo]
    ON [dbo].[LaundryOrders] ([OrderNo]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LaundryOrders_TenantName_StoreCode_CustCode'
      AND object_id = OBJECT_ID(N'[dbo].[LaundryOrders]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LaundryOrders_TenantName_StoreCode_CustCode]
    ON [dbo].[LaundryOrders] ([TenantName], [StoreCode], [CustCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);

            const string alterExistingColumnsSql = @"
IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'OrderMode') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders]
        ADD [OrderMode] [nvarchar](16) NOT NULL
            CONSTRAINT [DF_LaundryOrders_OrderMode] DEFAULT (N'pieces') WITH VALUES;
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'WeightInKg') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [WeightInKg] [decimal](18,3) NOT NULL CONSTRAINT [DF_LaundryOrders_WeightInKg_Alter] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'RatePerKg') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [RatePerKg] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_RatePerKg_Alter] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'SubTotal') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [SubTotal] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_SubTotal] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'TaxPercent') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [TaxPercent] [decimal](5,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TaxPercent] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'TaxAmount') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [TaxAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TaxAmount] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'CgstAmount') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [CgstAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_CgstAmount] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'SgstAmount') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [SgstAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_SgstAmount] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'TotalAmount') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [TotalAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_TotalAmount] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'InvoiceNo') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [InvoiceNo] [nvarchar](32) NULL;
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'Status') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [Status] [nvarchar](64) NULL;
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'PaidNow') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [PaidNow] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_PaidNow_Alter] DEFAULT((0));
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'PendingAmount') IS NULL
    BEGIN
        ALTER TABLE [dbo].[LaundryOrders] ADD [PendingAmount] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrders_PendingAmount_Alter] DEFAULT((0));
    END;
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(alterExistingColumnsSql);

            const string backfillPendingAmountSql = @"
IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'PendingAmount') IS NOT NULL
    BEGIN
        UPDATE [dbo].[LaundryOrders]
        SET [PendingAmount] = [NetPayable]
        WHERE [PendingAmount] = 0 AND [NetPayable] > 0;
    END;
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(backfillPendingAmountSql);
        }

        private async Task EnsureCustomerAdvancesTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[CustomerAdvances]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CustomerAdvances](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [CustCode] [nvarchar](64) NOT NULL,
        [AdvanceAmount] [decimal](18,2) NOT NULL,
        [TransactionType] [nvarchar](32) NOT NULL,
        [Notes] [nvarchar](500) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_CustomerAdvances] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CustomerAdvances_TenantName_StoreCode_CustCode'
      AND object_id = OBJECT_ID(N'[dbo].[CustomerAdvances]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_CustomerAdvances_TenantName_StoreCode_CustCode]
    ON [dbo].[CustomerAdvances] ([TenantName], [StoreCode], [CustCode]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private static string GenerateRandomID()
        {
            string srcrandomId = LMSRandom("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8);
            return srcrandomId;
        }

        private List<LaundryItemPriceEntity> BuildDefaultLaundryItemPrices(string tenantName, string storeCode)
        {
            var csvDefaults = TryLoadDefaultLaundryItemPricesFromCsv(tenantName, storeCode);
            if (csvDefaults.Count > 0)
            {
                return csvDefaults;
            }

            var defaults = new List<LaundryItemPriceEntity>
            {
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Men", ItemName = "Shirt", UnitPrice = 25, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Men", ItemName = "Pant", UnitPrice = 30, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Women", ItemName = "Top", UnitPrice = 24, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Women", ItemName = "Saree", UnitPrice = 70, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Kids", ItemName = "Kid Shirt", UnitPrice = 15, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Dry Clean", Category = "Household", ItemName = "Blanket", UnitPrice = 80, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Steam Iron", Category = "Men", ItemName = "Shirt", UnitPrice = 12, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Steam Iron", Category = "Women", ItemName = "Dress", UnitPrice = 16, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Wash & Fold", Category = "Men", ItemName = "Daily Wear", UnitPrice = 18, IsActive = true },
                new LaundryItemPriceEntity { TenantName = tenantName, StoreCode = storeCode, ServiceType = "Wash & Fold", Category = "Household", ItemName = "Mixed Load", UnitPrice = 50, IsActive = true }
            };

            return defaults;
        }

        private List<LaundryItemPriceEntity> TryLoadDefaultLaundryItemPricesFromCsv(string tenantName, string storeCode)
        {
            var result = new List<LaundryItemPriceEntity>();
            var seedFilePath = _seedFilePaths?.FirstOrDefault(File.Exists);

            if (string.IsNullOrWhiteSpace(seedFilePath))
            {
                return result;
            }

            var lines = File.ReadAllLines(seedFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                return result;
            }

            foreach (var rawLine in lines.Skip(1))
            {
                var parts = rawLine.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                var serviceType = parts[0].Trim();
                var category = parts[1].Trim();
                var itemName = parts[2].Trim();
                var unitPriceText = parts[3].Trim();

                if (string.IsNullOrWhiteSpace(serviceType)
                    || string.IsNullOrWhiteSpace(category)
                    || string.IsNullOrWhiteSpace(itemName))
                {
                    continue;
                }

                if (!decimal.TryParse(unitPriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice)
                    && !decimal.TryParse(unitPriceText, NumberStyles.Number, CultureInfo.CurrentCulture, out unitPrice))
                {
                    continue;
                }

                result.Add(new LaundryItemPriceEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = serviceType,
                    Category = category,
                    ItemName = itemName,
                    UnitPrice = unitPrice,
                    IsActive = true
                });
            }

            return result;
        }

        private static string GenerateOrderNo()
        {
            return $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{GenerateRandomID()}";
        }
        public static string LMSRandom(string pattern, int length, bool isPassword = false)
        {
            return GenerateDynamicCryptoString(pattern, length, isPassword);
        }
        public static string GenerateDynamicCryptoString(string pattern, int length, bool isPassword = false)
        {
            if (length != 0)
            {
                RNGCryptoServiceProvider provider = CreateRNGCryptoServiceProvider();
                StringBuilder sb;
                do
                {
                    sb = new StringBuilder();
                    var byteArray = new byte[length > 3 ? length : 4];
                    provider.GetBytes(byteArray);

                    int patternLength = pattern.Length;

                    //Gets character on index based from the pattern passed
                    for (var i = 0; i < byteArray.Length; i++)
                    {
                        byte x = byteArray[i];
                        while (x >= patternLength)
                            x = Convert.ToByte(x % patternLength);
                        sb.Append(pattern[x]);
                    }

                    //Below condition checks if atleast one special character is avaible in generated string.
                    isPassword = HasSpecialCharacters(sb, pattern, isPassword);
                } while (isPassword);

                return sb.ToString();
            }

            return string.Empty;
        }
        private static bool HasSpecialCharacters(StringBuilder sb, string pattern, bool isPassword)
        {
            //Below condition checks if atleast one special character is avaible in generated string.
            if (isPassword)
            {
                var regex = new Regex("[a-zA-Z0-9]*");
                var specialChars = regex.Replace(pattern, "");
                if (!string.IsNullOrEmpty(specialChars))
                {
                    var specialCharsRegex = new Regex("[" + specialChars + "]");
                    isPassword = !specialCharsRegex.IsMatch(sb.ToString());
                }
                else
                    isPassword = false;
            }
            return isPassword;
        }
       private static RNGCryptoServiceProvider CreateRNGCryptoServiceProvider()
        {
            RNGCryptoServiceProvider rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            return rngCryptoServiceProvider;
        }
        public interface ITenantCustomerRegistryConnection
        {
        string ConnectionString { get; set; }
        string DatabaseName { get; set; }
        string TenantCustomersCollectionName { get; set; }
        int ConnectTimeoutInSeconds { get; set; }
        }

         public class TenantCustomerRegistryConnection : ITenantCustomerRegistryConnection
        {
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        public string TenantCustomersCollectionName { get; set; }
        public int ConnectTimeoutInSeconds { get; set; }
        }
    }
}

