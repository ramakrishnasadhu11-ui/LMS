using AutoMapper;
using LMS.Master.BusinessSerive.Data;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.DTO.Entity;
using LMS.Master.Utilities;
using MailKit.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LMS.Master.BusinessSerive.Services
{
    public class Customerservice : ICustomer
    {
        private const string InternalWebhookSecretManagedKey = "__els_webhook_secret_managed";
        private const string InternalWebhookSecretRevealedKey = "__els_webhook_secret_revealed";
        private const string InternalWebhookSecretGeneratedAtKey = "__els_webhook_secret_generated_at_utc";
        private const string InternalWebhookSecretRevealedAtKey = "__els_webhook_secret_revealed_at_utc";

        private sealed class IntegrationSettingsPayload
        {
            public Dictionary<string, string> Public { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Secret { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StoreCurrencyConfiguration
        {
            public string CurrencyCode { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyLocale { get; set; }
            public int? CurrencyDecimalDigits { get; set; }
        }

        private sealed class RazorpayPaymentVerificationDetails
        {
            public string PaymentId { get; set; }
            public string OrderId { get; set; }
            public long AmountInMinorUnits { get; set; }
            public string Currency { get; set; }
            public string Status { get; set; }
        }

        private static readonly HashSet<string> RazorpaySupportedCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INR", "USD", "EUR", "GBP", "SGD", "AED", "AUD", "CAD", "CHF", "CNY",
            "JPY", "HKD", "NOK", "NZD", "SEK", "ZAR", "SAR", "QAR", "KWD", "BHD",
            "OMR", "MYR", "THB", "IDR", "PHP", "VND", "KRW", "DKK", "PLN", "BRL"
        };

        private static readonly HashSet<string> SupportedTaxModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NoTax",
            "GST",
            "VAT",
            "SalesTax"
        };

        private sealed class CountryTaxProfile
        {
            public string DefaultTaxMode { get; set; }
            public string DefaultCurrencyCode { get; set; }
        }

        private static readonly IReadOnlyDictionary<string, CountryTaxProfile> SupportedCountryTaxProfiles
            = new Dictionary<string, CountryTaxProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["IN"] = new CountryTaxProfile { DefaultTaxMode = "GST", DefaultCurrencyCode = "INR" },
                ["US"] = new CountryTaxProfile { DefaultTaxMode = "SalesTax", DefaultCurrencyCode = "USD" },
                ["GB"] = new CountryTaxProfile { DefaultTaxMode = "VAT", DefaultCurrencyCode = "GBP" },
                ["AE"] = new CountryTaxProfile { DefaultTaxMode = "VAT", DefaultCurrencyCode = "AED" },
                ["DE"] = new CountryTaxProfile { DefaultTaxMode = "VAT", DefaultCurrencyCode = "EUR" },
                ["FR"] = new CountryTaxProfile { DefaultTaxMode = "VAT", DefaultCurrencyCode = "EUR" }
            };

        private readonly IMapper _mapper;
        private readonly MasterDbContext _masterDbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Customerservice> _logger;
        private readonly string _integrationEncryptionKeyMaterial;
        private readonly List<string> _seedFilePaths;

        public Customerservice(IMapper mapper, MasterDbContext masterDbContext, IConfiguration configuration, ILogger<Customerservice> logger)
        {
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _masterDbContext = masterDbContext ?? throw new ArgumentNullException(nameof(masterDbContext));
            _configuration = configuration;
            _logger = logger;
            var encryptionKey = configuration?["IntegrationSettings:EncryptionKey"];
            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                encryptionKey = configuration?.GetConnectionString("MasterDb") ?? string.Empty;
            }

            _integrationEncryptionKeyMaterial = encryptionKey;
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

        public async Task<ActionReturnType> GetTenantIntegrationSettings(string tenantName, string storeCode, string category = null)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid integration settings request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

            var query = _masterDbContext.TenantIntegrationSettings
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(x => x.Category == category);
            }

            var records = await query
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ProviderDisplayName)
                .ToListAsync();

            var result = records
                .Select(MapToIntegrationDto)
                .ToList();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, result);
        }

        public async Task<ActionReturnType> SaveTenantIntegrationSettings(TenantIntegrationSettingsDto integrationDto)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (integrationDto == null
                || string.IsNullOrWhiteSpace(integrationDto.TenantName)
                || string.IsNullOrWhiteSpace(integrationDto.StoreCode)
                || string.IsNullOrWhiteSpace(integrationDto.Category)
                || string.IsNullOrWhiteSpace(integrationDto.ProviderKey))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid integration settings payload."
                });
            }

            integrationDto.TenantName = integrationDto.TenantName.Trim();
            integrationDto.StoreCode = integrationDto.StoreCode.Trim();
            integrationDto.Category = integrationDto.Category.Trim();
            integrationDto.ProviderKey = integrationDto.ProviderKey.Trim();
            integrationDto.ProviderDisplayName = string.IsNullOrWhiteSpace(integrationDto.ProviderDisplayName)
                ? integrationDto.ProviderKey
                : integrationDto.ProviderDisplayName.Trim();

            var isRazorpayConfiguration = IsRazorpayProvider(
                integrationDto.Category,
                integrationDto.ProviderKey,
                integrationDto.ProviderDisplayName,
                integrationDto.PublicSettings);

            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == integrationDto.TenantName
                && x.StoreCode == integrationDto.StoreCode
                && x.Category == integrationDto.Category
                && x.ProviderKey == integrationDto.ProviderKey);

            var previousPayload = existing == null
                ? new IntegrationSettingsPayload()
                : ReadPayload(existing.EncryptedSettingsJson);

            var payload = ClonePayload(previousPayload);

            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (integrationDto.PublicSettings != null)
            {
                foreach (var item in integrationDto.PublicSettings)
                {
                    var key = item.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var value = item.Value?.Trim();
                    if (isRazorpayConfiguration && key.Equals("WebhookUrl", StringComparison.OrdinalIgnoreCase))
                    {
                        // Razorpay webhook URL is server-generated and must never trust tenant input.
                        continue;
                    }

                    if (string.IsNullOrEmpty(value))
                    {
                        payload.Public.Remove(key);
                    }
                    else
                    {
                        payload.Public[key] = value;
                    }
                }

            if (isRazorpayConfiguration)
            {
                payload.Public.Remove("WebhookUrl");
                if (TryBuildRazorpayWebhookUrl(integrationDto.TenantName, integrationDto.StoreCode, integrationDto.ProviderKey, out var generatedWebhookUrl))
                {
                    payload.Public["WebhookUrl"] = generatedWebhookUrl;
                }
            }
            }

            if (integrationDto.ClearSecrets)
            {
                payload.Secret.Clear();
            }

            var webhookSecretProvidedByRequest = false;
            var previousWebhookSecret = previousPayload.Secret != null
                && previousPayload.Secret.TryGetValue("WebhookSecret", out var previousSecretValue)
                ? previousSecretValue?.Trim()
                : null;
            if (integrationDto.SecretSettings != null)
            {
                foreach (var item in integrationDto.SecretSettings)
                {
                    var key = item.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var value = item.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (key.StartsWith("__els_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (isRazorpayConfiguration
                        && key.Equals("WebhookSecret", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(previousWebhookSecret ?? string.Empty, value, StringComparison.Ordinal))
                    {
                        webhookSecretProvidedByRequest = true;
                    }

                    payload.Secret[key] = value;
                }
            }

            if (isRazorpayConfiguration)
            {
                if (webhookSecretProvidedByRequest)
                {
                    payload.Secret[InternalWebhookSecretManagedKey] = "false";
                    payload.Secret[InternalWebhookSecretRevealedKey] = "true";
                    payload.Secret.Remove(InternalWebhookSecretGeneratedAtKey);
                    payload.Secret.Remove(InternalWebhookSecretRevealedAtKey);
                }
                else if (!payload.Secret.TryGetValue("WebhookSecret", out var webhookSecret)
                    || string.IsNullOrWhiteSpace(webhookSecret))
                {
                    payload.Secret["WebhookSecret"] = GenerateRazorpayWebhookSecret();
                    payload.Secret[InternalWebhookSecretManagedKey] = "true";
                    payload.Secret[InternalWebhookSecretRevealedKey] = "false";
                    payload.Secret[InternalWebhookSecretGeneratedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    payload.Secret.Remove(InternalWebhookSecretRevealedAtKey);
                }
            }

            var encryptedPayload = EncryptSettingsPayload(payload);
            var hasSecrets = payload.Secret.Count > 0;
            var configurationChanged = existing == null
                || !PayloadEquals(previousPayload, payload)
                || existing.IsTestMode != integrationDto.IsTestMode
                || !string.Equals(existing.ProviderDisplayName ?? string.Empty, integrationDto.ProviderDisplayName ?? string.Empty, StringComparison.Ordinal);

            if (existing == null)
            {
                existing = new TenantIntegrationSettingsEntity
                {
                    TenantName = integrationDto.TenantName,
                    StoreCode = integrationDto.StoreCode,
                    Category = integrationDto.Category,
                    ProviderKey = integrationDto.ProviderKey,
                    ProviderDisplayName = integrationDto.ProviderDisplayName,
                    IsEnabled = false,
                    IsTestMode = integrationDto.IsTestMode,
                    EncryptedSettingsJson = encryptedPayload,
                    HasSecretsConfigured = hasSecrets,
                    LastTestStatus = "NotTested",
                    LastTestMessage = "Not tested yet.",
                    LastTestedAtUtc = null,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };

                _masterDbContext.TenantIntegrationSettings.Add(existing);
            }
            else
            {
                existing.ProviderDisplayName = integrationDto.ProviderDisplayName;
                existing.IsTestMode = integrationDto.IsTestMode;
                existing.EncryptedSettingsJson = encryptedPayload;
                existing.HasSecretsConfigured = hasSecrets;
                if (configurationChanged)
                {
                    existing.IsEnabled = false;
                    existing.LastTestStatus = "NotTested";
                    existing.LastTestMessage = "Not tested yet.";
                    existing.LastTestedAtUtc = null;
                }

                existing.ModifiedDate = DateTime.UtcNow;
            }

            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = configurationChanged
                    ? "Integration settings saved. Please test connection before enabling."
                    : "Integration settings saved successfully."
            });
        }

        public async Task<ActionReturnType> SetTenantIntegrationEnabled(string tenantName, string storeCode, string category, string providerKey, bool isEnabled)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid integration toggle request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = category.Trim();
            providerKey = providerKey.Trim();

            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Integration configuration not found. Save configuration first."
                });
            }

            if (isEnabled)
            {
                var payload = ReadPayload(existing.EncryptedSettingsJson);
                var isRazorpay = IsRazorpayProvider(category, existing.ProviderKey, existing.ProviderDisplayName, payload?.Public);
                if (isRazorpay)
                {
                    payload ??= new IntegrationSettingsPayload();
                    payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if ((!payload.Public.TryGetValue("WebhookUrl", out var persistedWebhookUrl)
                            || string.IsNullOrWhiteSpace(persistedWebhookUrl))
                        && TryBuildRazorpayWebhookUrl(existing.TenantName, existing.StoreCode, existing.ProviderKey, out var generatedWebhookUrl))
                    {
                        payload.Public["WebhookUrl"] = generatedWebhookUrl;
                    }
                }

                var validationErrors = ValidateIntegrationConfiguration(
                    category,
                    payload,
                    existing.ProviderKey,
                    existing.ProviderDisplayName,
                    requireRazorpayWebhookSecret: true);
                if (validationErrors.Count > 0)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = string.Join(" ", validationErrors)
                    });
                }

                if (!string.Equals(existing.LastTestStatus, "Success", StringComparison.OrdinalIgnoreCase))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "Integration can be enabled only after a successful connection test."
                    });
                }
            }

            existing.IsEnabled = isEnabled;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = isEnabled ? "Integration enabled successfully." : "Integration disabled successfully."
            });
        }

        public async Task<ActionReturnType> TestTenantIntegration(string tenantName, string storeCode, string category, string providerKey)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid integration test request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = category.Trim();
            providerKey = providerKey.Trim();

            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Integration configuration not found. Save configuration first."
                });
            }

            var payload = ReadPayload(existing.EncryptedSettingsJson);
            var isRazorpay = IsRazorpayProvider(category, existing.ProviderKey, existing.ProviderDisplayName, payload?.Public);
            if (isRazorpay)
            {
                payload ??= new IntegrationSettingsPayload();
                payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if ((!payload.Public.TryGetValue("WebhookUrl", out var persistedWebhookUrl)
                        || string.IsNullOrWhiteSpace(persistedWebhookUrl))
                    && TryBuildRazorpayWebhookUrl(existing.TenantName, existing.StoreCode, existing.ProviderKey, out var generatedWebhookUrl))
                {
                    payload.Public["WebhookUrl"] = generatedWebhookUrl;
                }
            }

            var validationErrors = ValidateIntegrationConfiguration(
                category,
                payload,
                existing.ProviderKey,
                existing.ProviderDisplayName,
                requireRazorpayWebhookSecret: false);

            existing.LastTestedAtUtc = DateTime.UtcNow;
            if (validationErrors.Count > 0)
            {
                existing.IsEnabled = false;
                existing.LastTestStatus = "Failed";
                existing.LastTestMessage = string.Join(" ", validationErrors).Trim();
                if (existing.LastTestMessage?.Length > 512)
                {
                    existing.LastTestMessage = existing.LastTestMessage.Substring(0, 512);
                }

                existing.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = existing.LastTestMessage
                });
            }

            var (probeSuccess, probeMessage) = await ProbeIntegrationConnectionAsync(category, providerKey, payload, existing.IsTestMode);
            if (!probeSuccess)
            {
                existing.IsEnabled = false;
                existing.LastTestStatus = "Failed";
                existing.LastTestMessage = string.IsNullOrWhiteSpace(probeMessage)
                    ? "Connection test failed."
                    : probeMessage;
                existing.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = existing.LastTestMessage
                });
            }

            existing.LastTestStatus = "Success";
            existing.LastTestMessage = string.IsNullOrWhiteSpace(probeMessage)
                ? "Configuration validated successfully. This test did not send live notifications or initiate payments."
                : probeMessage;
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = existing.LastTestMessage
            });
        }

        public async Task<ActionReturnType> RevealTenantIntegrationWebhookSecretOnce(string tenantName, string storeCode, string category, string providerKey)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid webhook secret reveal request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = category.Trim();
            providerKey = providerKey.Trim();

            var isRazorpayOrLegacyAlias = string.Equals(providerKey, "razorpay", StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerKey, "generic-payment", StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(category, IntegrationConstants.Category.Payments, StringComparison.OrdinalIgnoreCase)
                || !isRazorpayOrLegacyAlias)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret reveal is available only for Razorpay payment integration."
                });
            }

            await using var dbTransaction = await _masterDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing == null)
            {
                var fallbackProviderKey = string.Equals(providerKey, "generic-payment", StringComparison.OrdinalIgnoreCase)
                    ? "razorpay"
                    : "generic-payment";

                existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                    x.TenantName == tenantName
                    && x.StoreCode == storeCode
                    && x.Category == category
                    && x.ProviderKey == fallbackProviderKey);
            }

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Integration configuration not found. Save configuration first."
                });
            }

            var payload = ReadPayload(existing.EncryptedSettingsJson);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!IsRazorpayProvider(existing.Category, existing.ProviderKey, existing.ProviderDisplayName, payload.Public))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret reveal is available only for Razorpay payment integration."
                });
            }

            providerKey = existing.ProviderKey;

            if (!payload.Secret.TryGetValue("WebhookSecret", out var webhookSecret)
                || string.IsNullOrWhiteSpace(webhookSecret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret is not configured for this integration."
                });
            }

            if (!IsElsManagedWebhookSecret(payload.Secret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret was not generated by ELS. Rotate secret to create a managed one-time reveal secret."
                });
            }

            if (IsWebhookSecretAlreadyRevealed(payload.Secret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Webhook secret has already been revealed once. Rotate the secret to generate a new one-time secret."
                });
            }

            payload.Secret[InternalWebhookSecretRevealedKey] = "true";
            payload.Secret[InternalWebhookSecretRevealedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            existing.EncryptedSettingsJson = EncryptSettingsPayload(payload);
            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            _logger?.LogInformation(
                "Razorpay webhook secret revealed one-time for Tenant {TenantName}, Store {StoreCode}, Provider {ProviderKey}.",
                tenantName,
                storeCode,
                providerKey);

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new TenantIntegrationRevealWebhookSecretResponseDto
            {
                ProviderKey = providerKey,
                WebhookSecret = webhookSecret.Trim(),
                Message = "Webhook secret revealed once. Store it securely in Razorpay Dashboard and your secure vault."
            });
        }

        public async Task<ActionReturnType> RotateTenantIntegrationWebhookSecret(string tenantName, string storeCode, string category, string providerKey)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid webhook secret rotation request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = category.Trim();
            providerKey = providerKey.Trim();

            var isRazorpayOrLegacyAlias = string.Equals(providerKey, "razorpay", StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerKey, "generic-payment", StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(category, IntegrationConstants.Category.Payments, StringComparison.OrdinalIgnoreCase)
                || !isRazorpayOrLegacyAlias)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret rotation is available only for Razorpay payment integration."
                });
            }

            await using var dbTransaction = await _masterDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing == null)
            {
                var fallbackProviderKey = string.Equals(providerKey, "generic-payment", StringComparison.OrdinalIgnoreCase)
                    ? "razorpay"
                    : "generic-payment";

                existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                    x.TenantName == tenantName
                    && x.StoreCode == storeCode
                    && x.Category == category
                    && x.ProviderKey == fallbackProviderKey);
            }

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Integration configuration not found. Save configuration first."
                });
            }

            var payload = ReadPayload(existing.EncryptedSettingsJson);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!IsRazorpayProvider(existing.Category, existing.ProviderKey, existing.ProviderDisplayName, payload.Public))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook secret rotation is available only for Razorpay payment integration."
                });
            }

            providerKey = existing.ProviderKey;

            payload.Secret["WebhookSecret"] = GenerateRazorpayWebhookSecret();
            payload.Secret[InternalWebhookSecretManagedKey] = "true";
            payload.Secret[InternalWebhookSecretRevealedKey] = "false";
            payload.Secret[InternalWebhookSecretGeneratedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            payload.Secret.Remove(InternalWebhookSecretRevealedAtKey);

            existing.EncryptedSettingsJson = EncryptSettingsPayload(payload);
            existing.IsEnabled = false;
            existing.LastTestStatus = "NotTested";
            existing.LastTestMessage = "Webhook secret rotated. Please register the new webhook secret in Razorpay Dashboard, test connection, and then enable integration.";
            existing.LastTestedAtUtc = null;
            existing.ModifiedDate = DateTime.UtcNow;

            await _masterDbContext.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            _logger?.LogInformation(
                "Razorpay webhook secret rotated for Tenant {TenantName}, Store {StoreCode}, Provider {ProviderKey}.",
                tenantName,
                storeCode,
                providerKey);

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
            {
                StatusCode = "200",
                Message = "Webhook secret rotated. Retrieve it using one-time reveal and register in Razorpay Dashboard."
            });
        }

        public async Task<ActionReturnType> SendTenantIntegrationTestEmail(string tenantName, string storeCode, string category, string providerKey, string recipientEmail)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey)
                || string.IsNullOrWhiteSpace(recipientEmail))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid send test email request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            category = category.Trim();
            providerKey = providerKey.Trim();
            recipientEmail = recipientEmail.Trim();

            if (!string.Equals(category, IntegrationConstants.Category.Email, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(providerKey, "smtp", StringComparison.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Send test email is available only for Email/SMTP integration."
                });
            }

            try
            {
                recipientEmail = new MailAddress(recipientEmail).Address;
            }
            catch
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Recipient email address is invalid."
                });
            }
            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Integration configuration not found. Save configuration first."
                });
            }

            var payload = ReadPayload(existing.EncryptedSettingsJson);
            var validationErrors = ValidateIntegrationConfiguration(
                category,
                payload,
                existing.ProviderKey,
                existing.ProviderDisplayName,
                requireRazorpayWebhookSecret: false);
            if (validationErrors.Count > 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = string.Join(" ", validationErrors)
                });
            }

            payload ??= new IntegrationSettingsPayload();
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Public(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Public.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            string Secret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var host = Public("Host", "Endpoint");
            var portText = Public("Port", "AccountId");
            var senderEmail = Public("From", "SenderId");
            var username = Secret("Username", "ApiKey");
            var password = Secret("Password", "Token", "Secret", "SecretKey");

            if (!int.TryParse(portText, out var port) || port <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "SMTP port is invalid."
                });
            }

            try
            {
                senderEmail = new MailAddress(senderEmail).Address;
            }
            catch
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Sender email address is invalid."
                });
            }

            bool ResolveEnableSsl()
            {
                var sslText = Public("EnableSsl", "UseSsl", "Ssl", "Tls", "UseTls");
                if (bool.TryParse(sslText, out var boolValue))
                {
                    return boolValue;
                }

                if (!string.IsNullOrWhiteSpace(sslText))
                {
                    var normalized = sslText.Trim();
                    if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("on", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("required", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return port == 465 || port == 587;
            }

            var emailSubject = "Excel Laundry Services SMTP Test Email";
            var emailBody = "This is a test email sent from Excel Laundry Services SMTP integration settings.\n\n"
                + $"Tenant: {tenantName}\n"
                + $"Store: {storeCode}\n"
                + $"Sent UTC: {DateTime.UtcNow:O}";

            var portsToTry = new List<int> { port };
            if (string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!portsToTry.Contains(587))
                {
                    portsToTry.Add(587);
                }

                if (!portsToTry.Contains(465))
                {
                    portsToTry.Add(465);
                }
            }

            existing.LastTestedAtUtc = DateTime.UtcNow;

            string BuildSmtpFailureMessage(Exception ex)
            {
                if (ex == null)
                {
                    return "Failed to send test email.";
                }

                var primary = ex.Message?.Trim();
                var inner = ex.InnerException?.Message?.Trim();
                var primaryType = ex.GetType().Name;
                var innerType = ex.InnerException?.GetType().Name;

                if (string.IsNullOrWhiteSpace(inner))
                {
                    return $"Failed to send test email: {primaryType}: {primary}";
                }

                return $"Failed to send test email: {primaryType}: {primary} | Inner: {innerType}: {inner}";
            }

            Exception lastException = null;
            foreach (var portToTry in portsToTry)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress("Excel Laundry Services", senderEmail));
                    message.To.Add(MailboxAddress.Parse(recipientEmail));
                    message.Subject = emailSubject;
                    message.Body = new TextPart("plain") { Text = emailBody };

                    using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
                    {
                        smtpClient.Timeout = 15000;

                        var enableSsl = ResolveEnableSsl();
                        var socketOption = enableSsl
                            ? (portToTry == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                            : SecureSocketOptions.None;

                        await smtpClient.ConnectAsync(host, portToTry, socketOption);

                        if (!string.IsNullOrWhiteSpace(username))
                        {
                            await smtpClient.AuthenticateAsync(username, password ?? string.Empty);
                        }

                        await smtpClient.SendAsync(message);
                        await smtpClient.DisconnectAsync(true);
                    }

                    existing.LastTestStatus = "Success";
                    existing.LastTestMessage = $"Test email sent successfully to {recipientEmail}.";
                    existing.ModifiedDate = DateTime.UtcNow;
                    await _masterDbContext.SaveChangesAsync();

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                    {
                        StatusCode = "200",
                        Message = existing.LastTestMessage
                    });
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            existing.IsEnabled = false;
            existing.LastTestStatus = "Failed";
            existing.LastTestMessage = BuildSmtpFailureMessage(lastException);
            if (existing.LastTestMessage.Length > 512)
            {
                existing.LastTestMessage = existing.LastTestMessage.Substring(0, 512);
            }

            existing.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
            {
                StatusCode = "400",
                Message = existing.LastTestMessage
            });
        }

        public async Task<ActionReturnType> SendTenantIntegrationOrderBillEmail(TenantIntegrationOrderBillEmailRequestDto request)
        {
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (request == null
                || string.IsNullOrWhiteSpace(request.TenantName)
                || string.IsNullOrWhiteSpace(request.StoreCode)
                || string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid order bill email request."
                });
            }

            var tenantName = request.TenantName.Trim();
            var storeCode = request.StoreCode.Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? IntegrationConstants.Category.Email : request.Category.Trim();
            var providerKey = string.IsNullOrWhiteSpace(request.ProviderKey) ? "smtp" : request.ProviderKey.Trim();
            var recipientEmail = request.RecipientEmail.Trim();

            if (!string.Equals(category, IntegrationConstants.Category.Email, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(providerKey, "smtp", StringComparison.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Order bill email is available only for Email/SMTP integration."
                });
            }

            try
            {
                recipientEmail = new MailAddress(recipientEmail).Address;
            }
            catch
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Recipient email address is invalid."
                });
            }

            string GetConfig(params string[] keys)
            {
                foreach (var key in keys)
                {
                    var value = _configuration?[key];
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            static bool ParseEnableSsl(string sslText, int smtpPort)
            {
                if (bool.TryParse(sslText, out var boolValue))
                {
                    return boolValue;
                }

                if (!string.IsNullOrWhiteSpace(sslText))
                {
                    var normalized = sslText.Trim();
                    if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("on", StringComparison.OrdinalIgnoreCase)
                        || normalized.Equals("required", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return smtpPort == 465 || smtpPort == 587;
            }

            var host = GetConfig("MailConfiguration:Host", "MailConfiguration:SmtpServer");
            var portText = GetConfig("MailConfiguration:Port");
            var username = GetConfig("MailConfiguration:Username");
            var password = GetConfig("MailConfiguration:Password");
            var senderEmail = GetConfig("MailConfiguration:FromAddress", "MailConfiguration:MailFrom", "MailConfiguration:Sender");
            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                senderEmail = username;
            }

            var port = int.TryParse(portText, out var globalPort) && globalPort > 0 ? globalPort : 587;
            var enableSsl = ParseEnableSsl(GetConfig("MailConfiguration:EnableSsl"), port);

            var existing = await _masterDbContext.TenantIntegrationSettings.FirstOrDefaultAsync(x =>
                x.TenantName == tenantName
                && x.StoreCode == storeCode
                && x.Category == category
                && x.ProviderKey == providerKey);

            if (existing != null)
            {
                var payload = ReadPayload(existing.EncryptedSettingsJson);
                var validationErrors = ValidateIntegrationConfiguration(
                    category,
                    payload,
                    existing.ProviderKey,
                    existing.ProviderDisplayName,
                    requireRazorpayWebhookSecret: false);

                if (validationErrors.Count == 0)
                {
                    payload ??= new IntegrationSettingsPayload();
                    payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    string Public(params string[] keys)
                    {
                        foreach (var key in keys)
                        {
                            if (payload.Public.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                            {
                                return value.Trim();
                            }
                        }

                        return null;
                    }

                    string Secret(params string[] keys)
                    {
                        foreach (var key in keys)
                        {
                            if (payload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                            {
                                return value.Trim();
                            }
                        }

                        return null;
                    }

                    var tenantHost = Public("Host", "Endpoint");
                    var tenantPortText = Public("Port", "AccountId");
                    var tenantSenderEmail = Public("From", "SenderId");
                    var tenantUsername = Secret("Username", "ApiKey");
                    var tenantPassword = Secret("Password", "Token", "Secret", "SecretKey");

                    if (int.TryParse(tenantPortText, out var tenantPort) && tenantPort > 0
                        && !string.IsNullOrWhiteSpace(tenantHost)
                        && !string.IsNullOrWhiteSpace(tenantSenderEmail)
                        && !string.IsNullOrWhiteSpace(tenantUsername)
                        && !string.IsNullOrWhiteSpace(tenantPassword))
                    {
                        host = tenantHost;
                        port = tenantPort;
                        username = tenantUsername;
                        password = tenantPassword;
                        senderEmail = tenantSenderEmail;
                        enableSsl = ParseEnableSsl(Public("EnableSsl", "UseSsl", "Ssl", "Tls", "UseTls"), port);
                    }
                    else
                    {
                        _logger?.LogWarning("Tenant SMTP integration is incomplete for Tenant {TenantName}, Store {StoreCode}. Falling back to global MailConfiguration for order bill email.", tenantName, storeCode);
                    }
                }
                else
                {
                    _logger?.LogWarning("Tenant SMTP integration validation failed for Tenant {TenantName}, Store {StoreCode}. Falling back to global MailConfiguration for order bill email.", tenantName, storeCode);
                }
            }

            if (string.IsNullOrWhiteSpace(host)
                || port <= 0
                || string.IsNullOrWhiteSpace(senderEmail))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Email configuration is not available for order bill delivery."
                });
            }

            try
            {
                senderEmail = new MailAddress(senderEmail).Address;
            }
            catch
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Sender email address is invalid."
                });
            }

            byte[] attachmentBytes = null;
            if (!string.IsNullOrWhiteSpace(request.AttachmentContentBase64))
            {
                try
                {
                    attachmentBytes = Convert.FromBase64String(request.AttachmentContentBase64);
                }
                catch
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "Order bill attachment is invalid."
                    });
                }
            }

            var subject = string.IsNullOrWhiteSpace(request.Subject) ? "Laundry Invoice" : request.Subject.Trim();
            var textBody = request.TextBody ?? string.Empty;
            var htmlBody = request.HtmlBody;
            var attachmentFileName = string.IsNullOrWhiteSpace(request.AttachmentFileName)
                ? "Invoice.pdf"
                : request.AttachmentFileName.Trim();

            var portsToTry = new List<int> { port };
            if (string.Equals(host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!portsToTry.Contains(587))
                {
                    portsToTry.Add(587);
                }

                if (!portsToTry.Contains(465))
                {
                    portsToTry.Add(465);
                }
            }

            string BuildSmtpFailureMessage(Exception ex)
            {
                if (ex == null)
                {
                    return "Failed to send order bill email.";
                }

                var primary = ex.Message?.Trim();
                var inner = ex.InnerException?.Message?.Trim();
                var primaryType = ex.GetType().Name;
                var innerType = ex.InnerException?.GetType().Name;

                if (string.IsNullOrWhiteSpace(inner))
                {
                    return $"Failed to send order bill email: {primaryType}: {primary}";
                }

                return $"Failed to send order bill email: {primaryType}: {primary} | Inner: {innerType}: {inner}";
            }

            Exception lastException = null;
            foreach (var portToTry in portsToTry)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress("Excel Laundry Services", senderEmail));
                    message.To.Add(MailboxAddress.Parse(recipientEmail));
                    message.Subject = subject;

                    var builder = new BodyBuilder
                    {
                        TextBody = textBody
                    };

                    if (!string.IsNullOrWhiteSpace(htmlBody))
                    {
                        builder.HtmlBody = htmlBody;
                    }

                    if (attachmentBytes != null && attachmentBytes.Length > 0)
                    {
                        builder.Attachments.Add(attachmentFileName, attachmentBytes, MimeKit.ContentType.Parse("application/pdf"));
                    }

                    message.Body = builder.ToMessageBody();

                    using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
                    {
                        smtpClient.Timeout = 30000;

                        var socketOption = enableSsl
                            ? (portToTry == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                            : SecureSocketOptions.None;

                        await smtpClient.ConnectAsync(host, portToTry, socketOption);

                        if (!string.IsNullOrWhiteSpace(username))
                        {
                            await smtpClient.AuthenticateAsync(username, password ?? string.Empty);
                        }

                        await smtpClient.SendAsync(message);
                        await smtpClient.DisconnectAsync(true);
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                    {
                        StatusCode = "200",
                        Message = $"Order bill email sent successfully to {recipientEmail}."
                    });
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
            {
                StatusCode = "400",
                Message = BuildSmtpFailureMessage(lastException)
            });
        }

        private static IntegrationSettingsPayload ClonePayload(IntegrationSettingsPayload payload)
        {
            payload ??= new IntegrationSettingsPayload();
            return new IntegrationSettingsPayload
            {
                Public = payload.Public?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Secret = payload.Secret?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static bool PayloadEquals(IntegrationSettingsPayload left, IntegrationSettingsPayload right)
        {
            left ??= new IntegrationSettingsPayload();
            right ??= new IntegrationSettingsPayload();

            static bool dictEquals(Dictionary<string, string> a, Dictionary<string, string> b)
            {
                a ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                b ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (a.Count != b.Count)
                {
                    return false;
                }

                foreach (var kv in a)
                {
                    if (!b.TryGetValue(kv.Key, out var other))
                    {
                        return false;
                    }

                    if (!string.Equals(kv.Value ?? string.Empty, other ?? string.Empty, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            return dictEquals(left.Public, right.Public) && dictEquals(left.Secret, right.Secret);
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

        public async Task<ActionReturnType> GetTaxConfiguration(string tenantName, string storeCode)
        {
            await EnsureTaxInvoiceSettingsTableExistsAsync();
            await EnsureTaxInvoiceComponentsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax configuration request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var taxSettings = await _masterDbContext.TaxInvoiceSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (taxSettings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax configuration not found."
                });
            }

            var components = await _masterDbContext.TaxInvoiceComponents
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .ToListAsync();

            var resolved = ResolveTaxModeFromSettings(taxSettings);
            var response = BuildTaxConfigurationDto(taxSettings, components, resolved.ResolvedTaxMode, resolved.ResolutionSource);
            return ActionSet.ActionReturnType(HttpStatusCode.OK, response);
        }

        public async Task<ActionReturnType> UpsertTaxConfiguration(TaxConfigurationUpsertRequestDto request)
        {
            try
            {
                await EnsureTaxInvoiceSettingsTableExistsAsync();
                await EnsureTaxInvoiceComponentsTableExistsAsync();
            }
            catch (Exception ex)
            {
                var sqlEx = TryGetSqlException(ex);
                if (sqlEx != null)
                {
                    _logger?.LogError(
                        ex,
                        "UpsertTaxConfiguration table-initialization failed. Tenant={TenantName}, Store={StoreCode}, SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                        request?.TenantName ?? string.Empty,
                        request?.StoreCode ?? string.Empty,
                        sqlEx.Number,
                        sqlEx.State,
                        sqlEx.Class,
                        sqlEx.Procedure ?? string.Empty,
                        sqlEx.LineNumber,
                        sqlEx.Server ?? string.Empty,
                        sqlEx.Message);
                }
                else
                {
                    _logger?.LogError(
                        ex,
                        "UpsertTaxConfiguration table-initialization failed. Tenant={TenantName}, Store={StoreCode}",
                        request?.TenantName ?? string.Empty,
                        request?.StoreCode ?? string.Empty);
                }

                throw;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.TenantName) || string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax configuration request."
                });
            }

            request.TenantName = request.TenantName.Trim();
            request.StoreCode = request.StoreCode.Trim();
            request.TaxMode = NormalizeTaxMode(request.TaxMode);
            request.CountryCode = NormalizeCountryCode(request.CountryCode);
            request.CurrencyCode = NormalizeCurrencyCode(request.CurrencyCode);

            if (TryGetCountryTaxProfile(request.CountryCode, out var countryProfile))
            {
                if (string.IsNullOrWhiteSpace(request.TaxMode))
                {
                    request.TaxMode = countryProfile.DefaultTaxMode;
                }

                if (string.IsNullOrWhiteSpace(request.CurrencyCode))
                {
                    request.CurrencyCode = countryProfile.DefaultCurrencyCode;
                }
            }

            request.InvoicePrefix = string.IsNullOrWhiteSpace(request.InvoicePrefix)
                ? null
                : request.InvoicePrefix.Trim().ToUpperInvariant();
            request.Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? null
                : request.Notes.Trim();

            var validationError = ValidateTaxConfigurationRequest(request);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = validationError
                });
            }

            var existing = await _masterDbContext.TaxInvoiceSettings.FirstOrDefaultAsync(x =>
                x.TenantName == request.TenantName && x.StoreCode == request.StoreCode);

            if (existing == null)
            {
                existing = new TaxInvoiceSettingsEntity
                {
                    TenantName = request.TenantName,
                    StoreCode = request.StoreCode,
                    EnableTax = request.EnableTax ?? false,
                    GstPercent = request.GstPercent ?? 0,
                    PricesIncludeTax = request.PricesIncludeTax ?? request.IsPricesInclusive,
                    InvoicePrefix = string.IsNullOrWhiteSpace(request.InvoicePrefix)
                        ? BuildStoreInvoicePrefix(request.StoreCode)
                        : request.InvoicePrefix,
                    NextInvoiceNumber = request.NextInvoiceNumber.GetValueOrDefault(1),
                    InvoiceNumberPadding = request.InvoiceNumberPadding.GetValueOrDefault(4),
                    ResetInvoiceNumberYearly = request.ResetInvoiceNumberYearly.GetValueOrDefault(false),
                    Notes = request.Notes,
                    TaxMode = request.TaxMode,
                    CountryCode = request.CountryCode,
                    CurrencyCode = request.CurrencyCode,
                    IsPricesInclusive = request.IsPricesInclusive,
                    CreatedDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };

                _masterDbContext.TaxInvoiceSettings.Add(existing);
            }
            else
            {
                if (!TryApplyConcurrencyToken(request.ConcurrencyToken, existing.RowVersion, out var conflictMessage))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                    {
                        StatusCode = "409",
                        Message = conflictMessage
                    });
                }

                if (request.EnableTax.HasValue)
                {
                    existing.EnableTax = request.EnableTax.Value;
                }

                if (request.GstPercent.HasValue)
                {
                    existing.GstPercent = request.GstPercent.Value;
                }

                if (request.PricesIncludeTax.HasValue)
                {
                    existing.PricesIncludeTax = request.PricesIncludeTax.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.InvoicePrefix))
                {
                    existing.InvoicePrefix = request.InvoicePrefix.Length > 16
                        ? request.InvoicePrefix.Substring(0, 16)
                        : request.InvoicePrefix;
                }

                if (request.NextInvoiceNumber.HasValue && request.NextInvoiceNumber.Value > 0)
                {
                    existing.NextInvoiceNumber = request.NextInvoiceNumber.Value;
                }

                if (request.InvoiceNumberPadding.HasValue && request.InvoiceNumberPadding.Value >= 1 && request.InvoiceNumberPadding.Value <= 12)
                {
                    existing.InvoiceNumberPadding = request.InvoiceNumberPadding.Value;
                }

                if (request.ResetInvoiceNumberYearly.HasValue)
                {
                    existing.ResetInvoiceNumberYearly = request.ResetInvoiceNumberYearly.Value;
                }

                if (request.Notes != null)
                {
                    existing.Notes = request.Notes.Length > 300 ? request.Notes.Substring(0, 300) : request.Notes;
                }

                existing.TaxMode = request.TaxMode;
                existing.CountryCode = request.CountryCode;
                existing.CurrencyCode = request.CurrencyCode;
                existing.IsPricesInclusive = request.IsPricesInclusive;
                existing.ModifiedDate = DateTime.UtcNow;
            }

            try
            {
                await _masterDbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Tax configuration update conflict detected. Please retry."
                });
            }
            catch (Exception ex)
            {
                var sqlEx = TryGetSqlException(ex);
                if (sqlEx != null)
                {
                    _logger?.LogError(
                        ex,
                        "UpsertTaxConfiguration SaveChanges failed. Tenant={TenantName}, Store={StoreCode}, SqlErrorNumber={SqlErrorNumber}, SqlState={SqlState}, SqlClass={SqlClass}, Procedure={Procedure}, LineNumber={LineNumber}, Server={Server}, Message={SqlMessage}",
                        request?.TenantName ?? string.Empty,
                        request?.StoreCode ?? string.Empty,
                        sqlEx.Number,
                        sqlEx.State,
                        sqlEx.Class,
                        sqlEx.Procedure ?? string.Empty,
                        sqlEx.LineNumber,
                        sqlEx.Server ?? string.Empty,
                        sqlEx.Message);
                }
                else
                {
                    _logger?.LogError(
                        ex,
                        "UpsertTaxConfiguration SaveChanges failed. Tenant={TenantName}, Store={StoreCode}",
                        request?.TenantName ?? string.Empty,
                        request?.StoreCode ?? string.Empty);
                }

                throw;
            }

            return await GetTaxConfiguration(request.TenantName, request.StoreCode);
        }

        private static SqlException TryGetSqlException(Exception ex)
        {
            if (ex == null)
            {
                return null;
            }

            if (ex is SqlException direct)
            {
                return direct;
            }

            if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is SqlException dbSqlEx)
            {
                return dbSqlEx;
            }

            return ex.InnerException as SqlException;
        }

        public async Task<ActionReturnType> AddTaxInvoiceComponent(TaxInvoiceComponentUpsertRequestDto request)
        {
            await EnsureTaxInvoiceSettingsTableExistsAsync();
            await EnsureTaxInvoiceComponentsTableExistsAsync();

            if (request == null || string.IsNullOrWhiteSpace(request.TenantName) || string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax component request."
                });
            }

            request.TenantName = request.TenantName.Trim();
            request.StoreCode = request.StoreCode.Trim();
            request.Code = NormalizeTaxComponentCode(request.Code);
            request.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
            request.Jurisdiction = NormalizeJurisdiction(request.Jurisdiction);

            var validationError = ValidateTaxComponentRequest(request, isUpdate: false);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = validationError
                });
            }

            var settings = await _masterDbContext.TaxInvoiceSettings.FirstOrDefaultAsync(x =>
                x.TenantName == request.TenantName && x.StoreCode == request.StoreCode);
            if (settings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax configuration not found for selected store."
                });
            }

            var normalizedJurisdictionKey = NormalizeJurisdictionUniquenessKey(request.Jurisdiction);
            var duplicate = request.IsActive
                && await _masterDbContext.TaxInvoiceComponents
                    .AnyAsync(x => x.TenantName == request.TenantName
                                   && x.StoreCode == request.StoreCode
                                   && x.Code == request.Code
                                   && x.Priority == request.Priority
                                   && x.IsActive
                                   && EF.Property<string>(x, "JurisdictionNormalizedKey") == normalizedJurisdictionKey);
            if (duplicate)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "An active tax component with the same code, priority and jurisdiction already exists."
                });
            }

            var entity = new TaxInvoiceComponentEntity
            {
                TaxInvoiceSettingsId = settings.Id,
                TenantName = request.TenantName,
                StoreCode = request.StoreCode,
                Code = request.Code,
                DisplayName = request.DisplayName,
                RatePercent = request.RatePercent,
                Jurisdiction = request.Jurisdiction,
                Priority = request.Priority,
                IsCompound = request.IsCompound,
                IsActive = request.IsActive,
                EffectiveFromUtc = request.EffectiveFromUtc,
                EffectiveToUtc = request.EffectiveToUtc,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };

            _masterDbContext.TaxInvoiceComponents.Add(entity);
            await _masterDbContext.SaveChangesAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, MapTaxComponentDto(entity));
        }

        public async Task<ActionReturnType> UpdateTaxInvoiceComponent(TaxInvoiceComponentUpsertRequestDto request)
        {
            await EnsureTaxInvoiceComponentsTableExistsAsync();

            if (request == null
                || !request.ComponentId.HasValue
                || string.IsNullOrWhiteSpace(request.TenantName)
                || string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax component update request."
                });
            }

            request.TenantName = request.TenantName.Trim();
            request.StoreCode = request.StoreCode.Trim();
            request.Code = NormalizeTaxComponentCode(request.Code);
            request.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
            request.Jurisdiction = NormalizeJurisdiction(request.Jurisdiction);

            var validationError = ValidateTaxComponentRequest(request, isUpdate: true);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = validationError
                });
            }

            var entity = await _masterDbContext.TaxInvoiceComponents.FirstOrDefaultAsync(x =>
                x.Id == request.ComponentId.Value
                && x.TenantName == request.TenantName
                && x.StoreCode == request.StoreCode);

            if (entity == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax component not found."
                });
            }

            if (!TryApplyConcurrencyToken(request.ConcurrencyToken, entity.RowVersion, out var conflictMessage))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = conflictMessage
                });
            }

            var normalizedJurisdictionKey = NormalizeJurisdictionUniquenessKey(request.Jurisdiction);
            var duplicate = request.IsActive
                && await _masterDbContext.TaxInvoiceComponents
                    .AnyAsync(x => x.Id != entity.Id
                                   && x.TenantName == request.TenantName
                                   && x.StoreCode == request.StoreCode
                                   && x.Code == request.Code
                                   && x.Priority == request.Priority
                                   && x.IsActive
                                   && EF.Property<string>(x, "JurisdictionNormalizedKey") == normalizedJurisdictionKey);
            if (duplicate)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "An active tax component with the same code, priority and jurisdiction already exists."
                });
            }

            entity.Code = request.Code;
            entity.DisplayName = request.DisplayName;
            entity.RatePercent = request.RatePercent;
            entity.Jurisdiction = request.Jurisdiction;
            entity.Priority = request.Priority;
            entity.IsCompound = request.IsCompound;
            entity.IsActive = request.IsActive;
            entity.EffectiveFromUtc = request.EffectiveFromUtc;
            entity.EffectiveToUtc = request.EffectiveToUtc;
            entity.ModifiedDate = DateTime.UtcNow;

            try
            {
                await _masterDbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Tax component update conflict detected. Please retry."
                });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, MapTaxComponentDto(entity));
        }

        public async Task<ActionReturnType> DeactivateTaxInvoiceComponent(TaxInvoiceComponentDeactivateRequestDto request)
        {
            await EnsureTaxInvoiceComponentsTableExistsAsync();

            if (request == null
                || request.ComponentId <= 0
                || string.IsNullOrWhiteSpace(request.TenantName)
                || string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax component deactivation request."
                });
            }

            request.TenantName = request.TenantName.Trim();
            request.StoreCode = request.StoreCode.Trim();

            var entity = await _masterDbContext.TaxInvoiceComponents.FirstOrDefaultAsync(x =>
                x.Id == request.ComponentId
                && x.TenantName == request.TenantName
                && x.StoreCode == request.StoreCode);

            if (entity == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax component not found."
                });
            }

            if (!TryApplyConcurrencyToken(request.ConcurrencyToken, entity.RowVersion, out var conflictMessage))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = conflictMessage
                });
            }

            if (!entity.IsActive)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, MapTaxComponentDto(entity));
            }

            entity.IsActive = false;
            entity.EffectiveToUtc ??= DateTime.UtcNow;
            entity.ModifiedDate = DateTime.UtcNow;

            try
            {
                await _masterDbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Tax component deactivation conflict detected. Please retry."
                });
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, MapTaxComponentDto(entity));
        }

        public async Task<ActionReturnType> ResolveTaxMode(string tenantName, string storeCode)
        {
            await EnsureTaxInvoiceSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName) || string.IsNullOrWhiteSpace(storeCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid tax mode resolution request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();

            var taxSettings = await _masterDbContext.TaxInvoiceSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (taxSettings == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Tax configuration not found."
                });
            }

            var resolved = ResolveTaxModeFromSettings(taxSettings);

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new TaxModeResolutionDto
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                CountryCode = taxSettings.CountryCode,
                ResolvedTaxMode = resolved.ResolvedTaxMode,
                ResolutionSource = resolved.ResolutionSource,
                LegacyTaxEnabled = taxSettings.EnableTax,
                LegacyGstPercent = taxSettings.GstPercent
            });
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

            await using var balanceTxn = await _masterDbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var currentBalance = await _masterDbContext.CustomerAdvances
                .Where(x => x.TenantName == advanceDto.TenantName
                            && x.StoreCode == advanceDto.StoreCode
                            && x.CustCode == advanceDto.CustCode)
                .SumAsync(x => (decimal?)x.AdvanceAmount) ?? 0;

            var nextBalance = currentBalance + signedAmount;
            if (nextBalance < 0)
            {
                await balanceTxn.RollbackAsync();
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
            await balanceTxn.CommitAsync();

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
            await EnsurePaymentTransactionsTableExistsAsync();
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

            if (string.Equals(orderDto.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase))
            {
                var pieceSchemaReady = await IsPieceTagSchemaReadyAsync();
                if (!pieceSchemaReady)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.ServiceUnavailable, new CustomerIOResponse
                    {
                        StatusCode = "503",
                        Message = "Piece-tag schema is not deployed. Please run the approved database migration before creating piece-based orders."
                    });
                }
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

            await EnsurePricingRulesTableExistsAsync();
            var pricingRules = await _masterDbContext.PricingRules
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == orderDto.TenantName && x.StoreCode == orderDto.StoreCode);

            if (pricingRules != null && pricingRules.EnableMinimumOrder && pricingRules.MinimumOrderAmount > 0)
            {
                var currentOrderAmount = Math.Round(Math.Max(0, orderDto.OrderAmount), 2, MidpointRounding.AwayFromZero);
                var minimumOrderAmount = Math.Round(pricingRules.MinimumOrderAmount, 2, MidpointRounding.AwayFromZero);
                if (currentOrderAmount < minimumOrderAmount)
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = $"Minimum order amount is {minimumOrderAmount:0.00}. Current order amount is {currentOrderAmount:0.00}."
                    });
                }
            }

            var submittedItems = NormalizeSubmittedOrderItems(orderDto.Items);
            if (string.Equals(orderDto.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase) && submittedItems.Count == 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Please add at least one garment item for piece-based orders."
                });
            }

            var totalPieces = string.Equals(orderDto.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase)
                ? submittedItems.Sum(x => x.Quantity > 0 ? x.Quantity : 0)
                : 0;

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
            await EnsureTaxInvoiceComponentsTableExistsAsync();

            var taxSettings = await _masterDbContext.TaxInvoiceSettings.FirstOrDefaultAsync(x =>
                x.TenantName == orderDto.TenantName && x.StoreCode == orderDto.StoreCode);

            var resolvedTaxMode = ResolveTaxModeFromSettings(taxSettings).ResolvedTaxMode;
            var utcNow = DateTime.UtcNow;
            var configuredTaxComponents = await _masterDbContext.TaxInvoiceComponents
                .AsNoTracking()
                .Where(x => x.TenantName == orderDto.TenantName
                            && x.StoreCode == orderDto.StoreCode
                            && x.IsActive
                            && (!x.EffectiveFromUtc.HasValue || x.EffectiveFromUtc.Value <= utcNow)
                            && (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc.Value >= utcNow))
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.Code)
                .ToListAsync();

            var taxCalculation = TaxCalculationHelper.Calculate(new TaxCalculationRequest
            {
                OrderAmount = orderDto.OrderAmount,
                TaxMode = resolvedTaxMode,
                EnableTax = taxSettings?.EnableTax ?? false,
                PricesIncludeTax = taxSettings?.IsPricesInclusive ?? taxSettings?.PricesIncludeTax ?? false,
                GstPercent = taxSettings?.GstPercent ?? 0m,
                Components = configuredTaxComponents.Select(x => new TaxCalculationComponent
                {
                    Code = x.Code,
                    DisplayName = x.DisplayName,
                    RatePercent = x.RatePercent,
                    Priority = x.Priority,
                    IsCompound = x.IsCompound,
                    IsActive = x.IsActive
                })
            });

            var computedTaxPercent = Math.Round(Math.Max(0m, taxCalculation.ComponentBreakdowns.Sum(x => x.RatePercent)), 2, MidpointRounding.AwayFromZero);
            var computedCgst = 0m;
            var computedSgst = 0m;

            if (string.Equals(taxCalculation.TaxMode, "GST", StringComparison.OrdinalIgnoreCase))
            {
                computedCgst = Math.Round(Math.Max(0m, taxCalculation.ComponentBreakdowns
                    .Where(x => string.Equals(x.Code, "CGST", StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.TaxAmount)), 2, MidpointRounding.AwayFromZero);

                computedSgst = Math.Round(Math.Max(0m, taxCalculation.ComponentBreakdowns
                    .Where(x => string.Equals(x.Code, "SGST", StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.TaxAmount)), 2, MidpointRounding.AwayFromZero);

                if (computedCgst == 0m && computedSgst == 0m && taxCalculation.TotalTax > 0m)
                {
                    computedCgst = Math.Round(taxCalculation.TotalTax / 2m, 2, MidpointRounding.AwayFromZero);
                    computedSgst = Math.Round(taxCalculation.TotalTax - computedCgst, 2, MidpointRounding.AwayFromZero);
                }
            }

            orderDto.AdvanceUsed = Math.Min(Math.Max(0, orderDto.AdvanceUsed), taxCalculation.TotalAmount);
            var netAfterAdvance = Math.Max(0, taxCalculation.TotalAmount - orderDto.AdvanceUsed);
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
            var persistedItems = new List<LaundryOrderItemDto>();
            LaundryOrderEntity orderEntity = null;

            await using (var tx = await _masterDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                orderEntity = new LaundryOrderEntity
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
                    SubTotal = taxCalculation.SubTotal,
                    TaxPercent = computedTaxPercent,
                    TaxAmount = taxCalculation.TotalTax,
                    CgstAmount = computedCgst,
                    SgstAmount = computedSgst,
                    TotalAmount = taxCalculation.TotalAmount,
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
                await _masterDbContext.SaveChangesAsync();

                if (taxCalculation.ComponentBreakdowns.Count > 0)
                {
                    var currencyCode = NormalizeCurrencyCode(taxSettings?.CurrencyCode);
                    if (string.IsNullOrWhiteSpace(currencyCode) && TryGetCountryTaxProfile(taxSettings?.CountryCode, out var profile))
                    {
                        currencyCode = profile.DefaultCurrencyCode;
                    }

                    if (string.IsNullOrWhiteSpace(currencyCode))
                    {
                        currencyCode = "INR";
                    }

                    var lineNo = 1;
                    foreach (var component in taxCalculation.ComponentBreakdowns)
                    {
                        _masterDbContext.LaundryOrderTaxLines.Add(new LaundryOrderTaxLineEntity
                        {
                            LaundryOrderId = orderEntity.Id,
                            TenantName = orderEntity.TenantName,
                            StoreCode = orderEntity.StoreCode,
                            OrderNo = orderEntity.OrderNo,
                            LineNo = lineNo++,
                            TaxMode = NormalizeTaxMode(taxCalculation.TaxMode) ?? "NoTax",
                            CurrencyCode = currencyCode,
                            ComponentCode = NormalizeTaxComponentCode(component.Code) ?? "TAX",
                            ComponentDisplayName = string.IsNullOrWhiteSpace(component.DisplayName) ? (component.Code ?? "Tax") : component.DisplayName.Trim(),
                            Jurisdiction = null,
                            Priority = component.Priority,
                            IsCompound = component.IsCompound,
                            RatePercent = component.RatePercent,
                            TaxableBaseAmount = Math.Round(component.TaxableAmount, 4, MidpointRounding.AwayFromZero),
                            TaxAmount = Math.Round(component.TaxAmount, 2, MidpointRounding.AwayFromZero)
                        });
                    }
                }

                if (string.Equals(orderDto.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase) && submittedItems.Count > 0)
                {
                    var tagSettings = await _masterDbContext.BarcodeTagSettings
                        .FirstOrDefaultAsync(x => x.TenantName == orderDto.TenantName && x.StoreCode == orderDto.StoreCode);

                    if (tagSettings == null)
                    {
                        tagSettings = new BarcodeTagSettingsEntity
                        {
                            TenantName = orderDto.TenantName,
                            StoreCode = orderDto.StoreCode,
                            EnableTagging = true,
                            TagPrefix = BuildStoreInvoicePrefix(orderDto.StoreCode),
                            NextTagNumber = 1,
                            TagNumberPadding = 4,
                            ResetTagNumberYearly = false,
                            Notes = null,
                            CreatedDate = DateTime.UtcNow,
                            ModifiedDate = DateTime.UtcNow
                        };

                        _masterDbContext.BarcodeTagSettings.Add(tagSettings);
                    }

                    var tagSequence = tagSettings.NextTagNumber < 1 ? 1 : tagSettings.NextTagNumber;
                    var tagPadding = tagSettings.TagNumberPadding < 1 || tagSettings.TagNumberPadding > 12
                        ? 4
                        : tagSettings.TagNumberPadding;
                    var taggingEnabled = tagSettings.EnableTagging;

                    if (!taggingEnabled)
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                        {
                            StatusCode = "400",
                            Message = "Tagging is disabled for this store. Enable barcode/tag settings before creating piece-based orders."
                        });
                    }

                    foreach (var item in submittedItems)
                    {
                        var qty = item.Quantity > 0 ? item.Quantity : 1;
                        for (var pieceNo = 1; pieceNo <= qty; pieceNo++)
                        {
                            string tagNo = null;
                            if (taggingEnabled)
                            {
                                tagNo = BuildPrintableTagNumber(tagSettings.TagPrefix, tagSequence, tagPadding);
                                tagSequence++;
                            }

                            _masterDbContext.LaundryOrderItems.Add(new LaundryOrderItemEntity
                            {
                                TenantName = orderDto.TenantName,
                                StoreCode = orderDto.StoreCode,
                                OrderNo = orderNo,
                                ServiceType = string.IsNullOrWhiteSpace(item.ServiceType) ? orderDto.ServiceType : item.ServiceType,
                                Category = item.Category,
                                ItemName = item.ItemName,
                                UnitPrice = item.UnitPrice,
                                PieceNo = pieceNo,
                                TagNo = tagNo,
                                CreatedDate = DateTime.UtcNow
                            });

                            persistedItems.Add(new LaundryOrderItemDto
                            {
                                ServiceType = string.IsNullOrWhiteSpace(item.ServiceType) ? orderDto.ServiceType : item.ServiceType,
                                Category = item.Category,
                                ItemName = item.ItemName,
                                UnitPrice = item.UnitPrice,
                                Quantity = 1,
                                PieceNo = pieceNo,
                                TagNo = tagNo
                            });
                        }
                    }

                    if (taggingEnabled)
                    {
                        tagSettings.NextTagNumber = tagSequence;
                        tagSettings.ModifiedDate = DateTime.UtcNow;
                    }
                }

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
                await tx.CommitAsync();
            }

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
                CreatedDate = orderEntity.CreatedDate,
                TotalPieces = totalPieces,
                Items = persistedItems
            });
        }

        private async Task<bool> IsPieceTagSchemaReadyAsync()
        {
            const string sql = @"
SELECT
    CASE
        WHEN OBJECT_ID(N'[dbo].[LaundryOrderItems]', N'U') IS NULL THEN 0
        WHEN OBJECT_ID(N'[dbo].[BarcodeTagSettings]', N'U') IS NULL THEN 0
        WHEN NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
              AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
              AND is_unique = 1
        ) THEN 0
        ELSE 1
    END";

            var connection = _masterDbContext.Database.GetDbConnection();
            var openedHere = false;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                openedHere = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();

            if (openedHere)
            {
                await connection.CloseAsync();
            }

            return value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
        }

        public async Task<ActionReturnType> CreateLaundryInvoicePaymentOrder(string tenantName, string storeCode, CreateLaundryInvoicePaymentOrderRequestDto request)
        {
            await EnsureLaundryOrdersTableExistsAsync();
            await EnsurePaymentTransactionsTableExistsAsync();
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || request == null
                || string.IsNullOrWhiteSpace(request.OrderNo)
                || string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid payment order request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            request.OrderNo = request.OrderNo.Trim();
            request.ClientRequestId = request.ClientRequestId.Trim();

            if (request.ClientRequestId.Length > 64)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Client request ID must be 64 characters or less."
                });
            }

            var order = await _masterDbContext.LaundryOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName
                                          && x.StoreCode == storeCode
                                          && x.OrderNo == request.OrderNo);

            if (order == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Order not found for this store."
                });
            }

            var payableAmount = Math.Round(Math.Max(0, order.NetPayable), 2, MidpointRounding.AwayFromZero);
            if (payableAmount <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Order is already paid or has no pending amount."
                });
            }

            var (currencyResolved, currencyError, currencyConfiguration) = await ResolveStoreCurrencyConfigurationAsync(tenantName, storeCode);
            if (!currencyResolved)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = string.IsNullOrWhiteSpace(currencyError)
                        ? "Currency configuration is not available for this store."
                        : currencyError
                });
            }

            var configuredCurrencyCode = currencyConfiguration.CurrencyCode.Trim().ToUpperInvariant();
            var configuredCurrencySymbol = currencyConfiguration.CurrencySymbol?.Trim();
            var configuredCurrencyLocale = currencyConfiguration.CurrencyLocale?.Trim();
            var configuredCurrencyDecimalDigits = currencyConfiguration.CurrencyDecimalDigits.Value;

            if (!TryConvertToMinorUnits(payableAmount, configuredCurrencyDecimalDigits, out var amountInMinorUnits)
                || amountInMinorUnits <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Order amount precision is incompatible with configured currency minor units."
                });
            }

            var integrationRecords = await _masterDbContext.TenantIntegrationSettings
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.Category == IntegrationConstants.Category.Payments
                            && x.IsEnabled)
                .ToListAsync();

            TenantIntegrationSettingsEntity razorpayIntegration = null;
            IntegrationSettingsPayload integrationPayload = null;
            foreach (var record in integrationRecords)
            {
                var payload = ReadPayload(record.EncryptedSettingsJson);
                if (IsRazorpayProvider(record.Category, record.ProviderKey, record.ProviderDisplayName, payload.Public))
                {
                    razorpayIntegration = record;
                    integrationPayload = payload;
                    break;
                }
            }

            if (razorpayIntegration == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Enabled Razorpay configuration not found for this store."
                });
            }

            integrationPayload ??= new IntegrationSettingsPayload();
            integrationPayload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Secret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (integrationPayload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var isTestMode = razorpayIntegration.IsTestMode;
            var keyId = isTestMode
                ? Secret("TestKeyId", "Username")
                : Secret("LiveKeyId", "LiveApiKey", "ApiKey", "Username", "Token", "KeyId");
            var keySecret = isTestMode
                ? Secret("TestKeySecret", "Password")
                : Secret("LiveKeySecret", "LiveSecretKey", "SecretKey", "Password", "Secret", "KeySecret");

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = isTestMode
                        ? "Razorpay test mode requires TestKeyId and TestKeySecret."
                        : "Razorpay credentials are missing for the selected mode."
                });
            }

            CreateLaundryInvoicePaymentOrderResponseDto BuildSafeResponse(PaymentTransactionsEntity tx)
                => new CreateLaundryInvoicePaymentOrderResponseDto
                {
                    TransactionId = tx.Id,
                    GatewayOrderId = tx.GatewayOrderId,
                    RazorpayKeyId = keyId,
                    AmountInMinorUnits = amountInMinorUnits,
                    Currency = configuredCurrencyCode,
                    CurrencySymbol = configuredCurrencySymbol,
                    CurrencyLocale = configuredCurrencyLocale,
                    CurrencyDecimalDigits = configuredCurrencyDecimalDigits,
                    InvoiceNo = order.InvoiceNo,
                    OrderNo = order.OrderNo,
                    IsTestMode = isTestMode
                };

            bool IsCompatibleIdempotentTransaction(PaymentTransactionsEntity tx)
            {
                if (tx == null)
                {
                    return false;
                }

                if (!string.Equals(tx.OrderNo?.Trim(), order.OrderNo, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.Equals(tx.Currency?.Trim(), configuredCurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var existingAmount = Math.Round(tx.AmountAuthorized, 2, MidpointRounding.AwayFromZero);
                return existingAmount == payableAmount;
            }

            var existing = await _masterDbContext.PaymentTransactions
                .FirstOrDefaultAsync(x => x.TenantName == tenantName
                                          && x.StoreCode == storeCode
                                          && x.ClientRequestId == request.ClientRequestId);

            if (existing != null)
            {
                if (!IsCompatibleIdempotentTransaction(existing))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                    {
                        StatusCode = "409",
                        Message = "This client request ID is already associated with a different order amount or currency context."
                    });
                }

                if (!string.IsNullOrWhiteSpace(existing.GatewayOrderId))
                {
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildSafeResponse(existing));
                }

                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "A payment request with this client request ID is already being processed."
                });
            }

            var now = DateTime.UtcNow;
            var transaction = new PaymentTransactionsEntity
            {
                TenantName = tenantName,
                StoreCode = storeCode,
                OrderNo = order.OrderNo,
                InvoiceNo = order.InvoiceNo,
                CustCode = order.CustCode,
                GatewayProvider = "Razorpay",
                ClientRequestId = request.ClientRequestId,
                GatewayOrderId = null,
                GatewayPaymentId = null,
                GatewaySignature = null,
                Currency = configuredCurrencyCode,
                AmountRequested = payableAmount,
                AmountAuthorized = payableAmount,
                AmountCaptured = 0,
                Status = "Initiating",
                FailureCode = null,
                FailureReason = null,
                IsLiveMode = !isTestMode,
                CreatedDate = now,
                ModifiedDate = now
            };

            _masterDbContext.PaymentTransactions.Add(transaction);
            try
            {
                await _masterDbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                var duplicate = await _masterDbContext.PaymentTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantName == tenantName
                                              && x.StoreCode == storeCode
                                              && x.ClientRequestId == request.ClientRequestId);
                if (duplicate != null)
                {
                    if (!IsCompatibleIdempotentTransaction(duplicate))
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                        {
                            StatusCode = "409",
                            Message = "This client request ID is already associated with a different order amount or currency context."
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(duplicate.GatewayOrderId))
                    {
                        return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildSafeResponse(duplicate));
                    }

                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                    {
                        StatusCode = "409",
                        Message = "A payment request with this client request ID is already being processed."
                    });
                }

                return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new CustomerIOResponse
                {
                    StatusCode = "500",
                    Message = "Unable to create payment transaction. Please try again."
                });
            }

            var razorpayApiBaseUrl = _configuration?["IntegrationSettings:RazorpayApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(razorpayApiBaseUrl))
            {
                razorpayApiBaseUrl = "https://api.razorpay.com";
            }

            if (!Uri.TryCreate(razorpayApiBaseUrl.Trim(), UriKind.Absolute, out var razorpayBaseUri)
                || razorpayBaseUri.Scheme != Uri.UriSchemeHttps)
            {
                transaction.Status = "Failed";
                transaction.FailureCode = "Config";
                transaction.FailureReason = "Razorpay API base URL is invalid.";
                transaction.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Razorpay API base URL is invalid."
                });
            }

            var receipt = request.ClientRequestId.Length > 40
                ? request.ClientRequestId.Substring(0, 40)
                : request.ClientRequestId;

            var orderCreatePayload = new
            {
                amount = amountInMinorUnits,
                currency = configuredCurrencyCode,
                receipt,
                notes = new
                {
                    orderNo = order.OrderNo,
                    invoiceNo = order.InvoiceNo ?? string.Empty
                }
            };

            var requestJson = JsonSerializer.Serialize(orderCreatePayload);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(razorpayBaseUri, "/v1/orders"));
            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            try
            {
                using var httpResponse = await httpClient.SendAsync(httpRequest);
                var responseBody = await httpResponse.Content.ReadAsStringAsync();
                if (!httpResponse.IsSuccessStatusCode)
                {
                    var (errorCode, errorMessage) = ParseRazorpayError(responseBody);
                    transaction.Status = "Failed";
                    transaction.FailureCode = string.IsNullOrWhiteSpace(errorCode)
                        ? ((int)httpResponse.StatusCode).ToString(CultureInfo.InvariantCulture)
                        : errorCode;
                    transaction.FailureReason = string.IsNullOrWhiteSpace(errorMessage)
                        ? $"Razorpay request failed with status code {(int)httpResponse.StatusCode}."
                        : errorMessage;
                    transaction.ModifiedDate = DateTime.UtcNow;
                    await _masterDbContext.SaveChangesAsync();

                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = transaction.FailureReason
                    });
                }

                using var json = JsonDocument.Parse(responseBody);
                var root = json.RootElement;
                var gatewayOrderId = root.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()?.Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(gatewayOrderId))
                {
                    transaction.Status = "Failed";
                    transaction.FailureCode = "InvalidResponse";
                    transaction.FailureReason = "Razorpay did not return a valid order ID.";
                    transaction.ModifiedDate = DateTime.UtcNow;
                    await _masterDbContext.SaveChangesAsync();

                    return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new CustomerIOResponse
                    {
                        StatusCode = "502",
                        Message = "Razorpay order creation failed due to invalid gateway response."
                    });
                }

                transaction.GatewayOrderId = gatewayOrderId;
                transaction.Status = "Created";
                transaction.FailureCode = null;
                transaction.FailureReason = null;
                transaction.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.OK, BuildSafeResponse(transaction));
            }
            catch (TaskCanceledException)
            {
                transaction.Status = "Failed";
                transaction.FailureCode = "Timeout";
                transaction.FailureReason = "Razorpay request timed out.";
                transaction.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new CustomerIOResponse
                {
                    StatusCode = "502",
                    Message = "Razorpay request timed out. Please retry."
                });
            }
            catch (HttpRequestException)
            {
                transaction.Status = "Failed";
                transaction.FailureCode = "Network";
                transaction.FailureReason = "Unable to reach Razorpay API.";
                transaction.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.BadGateway, new CustomerIOResponse
                {
                    StatusCode = "502",
                    Message = "Unable to reach Razorpay API. Please retry."
                });
            }
            catch (DbUpdateException)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Duplicate payment transaction detected. Please retry with a new client request ID."
                });
            }
            catch
            {
                transaction.Status = "Failed";
                transaction.FailureCode = "Unexpected";
                transaction.FailureReason = "Unexpected error while creating Razorpay order.";
                transaction.ModifiedDate = DateTime.UtcNow;
                await _masterDbContext.SaveChangesAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new CustomerIOResponse
                {
                    StatusCode = "500",
                    Message = "Unable to create payment order at this time."
                });
            }
        }

        public async Task<ActionReturnType> ConfirmLaundryInvoicePayment(string tenantName, string storeCode, ConfirmLaundryInvoicePaymentRequestDto request)
        {
            await EnsureLaundryOrdersTableExistsAsync();
            await EnsurePaymentTransactionsTableExistsAsync();
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || request == null
                || request.TransactionId <= 0
                || string.IsNullOrWhiteSpace(request.RazorpayOrderId)
                || string.IsNullOrWhiteSpace(request.RazorpayPaymentId)
                || string.IsNullOrWhiteSpace(request.RazorpaySignature))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid payment confirmation request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            request.RazorpayOrderId = request.RazorpayOrderId.Trim();
            request.RazorpayPaymentId = request.RazorpayPaymentId.Trim();
            request.RazorpaySignature = request.RazorpaySignature.Trim();

            var transactionSnapshot = await _masterDbContext.PaymentTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.TransactionId
                                          && x.TenantName == tenantName
                                          && x.StoreCode == storeCode);

            if (transactionSnapshot == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Payment transaction not found for this store."
                });
            }

            if (string.IsNullOrWhiteSpace(transactionSnapshot.GatewayOrderId)
                || !string.Equals(transactionSnapshot.GatewayOrderId, request.RazorpayOrderId, StringComparison.Ordinal))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid order reference for payment confirmation."
                });
            }

            var transactionCurrencyCode = transactionSnapshot.Currency?.Trim()?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(transactionCurrencyCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Saved payment transaction has no currency information."
                });
            }

            var configuredCurrencyDecimalDigits = GetCurrencyDecimalDigitsForCurrencyCode(transactionCurrencyCode);

            if (!TryConvertToMinorUnits(transactionSnapshot.AmountRequested, configuredCurrencyDecimalDigits, out var expectedAmountInMinorUnits)
                || expectedAmountInMinorUnits <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Saved payment amount is invalid for the configured currency."
                });
            }

            var (credentialsResolved, keyId, keySecret, isTestMode, credentialsError) = await ResolveRazorpayCredentialsAsync(tenantName, storeCode);
            if (!credentialsResolved)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = credentialsError
                });
            }

            var expectedSignature = ComputeRazorpayCheckoutSignature(request.RazorpayOrderId, request.RazorpayPaymentId, keySecret);
            if (!SecureEquals(expectedSignature, request.RazorpaySignature))
            {
                await MarkTransactionFailedAsync(transactionSnapshot.Id, "InvalidSignature", "Checkout signature verification failed.");
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Payment signature verification failed."
                });
            }

            var (paymentVerified, definitiveGatewayFailure, paymentStatusCode, paymentError, paymentDetails) = await FetchRazorpayPaymentDetailsAsync(keyId, keySecret, request.RazorpayPaymentId);
            if (!paymentVerified)
            {
                if (definitiveGatewayFailure)
                {
                    await MarkTransactionFailedAsync(transactionSnapshot.Id, "GatewayVerifyFailed", paymentError ?? "Unable to verify payment from gateway.");
                }

                return ActionSet.ActionReturnType(paymentStatusCode, new CustomerIOResponse
                {
                    StatusCode = ((int)paymentStatusCode).ToString(CultureInfo.InvariantCulture),
                    Message = paymentError ?? "Unable to verify payment from gateway."
                });
            }

            if (!string.Equals(paymentDetails.PaymentId, request.RazorpayPaymentId, StringComparison.Ordinal)
                || !string.Equals(paymentDetails.OrderId, transactionSnapshot.GatewayOrderId, StringComparison.Ordinal)
                || !string.Equals(paymentDetails.Currency?.Trim(), transactionCurrencyCode, StringComparison.OrdinalIgnoreCase)
                || paymentDetails.AmountInMinorUnits != expectedAmountInMinorUnits)
            {
                await MarkTransactionFailedAsync(transactionSnapshot.Id, "GatewayMismatch", "Gateway payment data does not match the saved transaction.");
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Gateway payment details did not match the saved transaction."
                });
            }

            if (!string.Equals(paymentDetails.Status, "captured", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(paymentDetails.Status, "failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paymentDetails.Status, "refunded", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paymentDetails.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    await MarkTransactionFailedAsync(transactionSnapshot.Id, "NotCaptured", "Gateway payment is not captured in a terminal state.");
                }

                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Payment is not captured yet."
                });
            }

            if (!TryConvertFromMinorUnits(paymentDetails.AmountInMinorUnits, configuredCurrencyDecimalDigits, out var capturedAmount))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Captured payment amount format is invalid for configured currency."
                });
            }

            return await FinalizeCapturedPaymentAsync(
                tenantName,
                storeCode,
                request.TransactionId,
                request.RazorpayOrderId,
                request.RazorpayPaymentId,
                request.RazorpaySignature,
                capturedAmount,
                isTestMode);
        }

        public async Task<ActionReturnType> ReceivePaymentWebhook(string provider, string referenceToken, string signature, string rawRequestBody)
        {
            await EnsureLaundryOrdersTableExistsAsync();
            await EnsurePaymentTransactionsTableExistsAsync();
            await EnsureTenantIntegrationSettingsTableExistsAsync();

            provider = NormalizeKey(provider).ToLowerInvariant();
            referenceToken = NormalizeKey(referenceToken).ToLowerInvariant();
            signature = NormalizeKey(signature);
            rawRequestBody ??= string.Empty;

            if (!string.Equals(provider, "razorpay", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(referenceToken)
                || string.IsNullOrWhiteSpace(signature)
                || string.IsNullOrWhiteSpace(rawRequestBody))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid webhook request."
                });
            }

            var (resolved, integration, payload, webhookSecret, resolveError) = await ResolveRazorpayWebhookIntegrationAsync(provider, referenceToken);
            if (!resolved)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = string.IsNullOrWhiteSpace(resolveError) ? "Invalid webhook context." : resolveError
                });
            }

            var expectedWebhookSignature = ComputeRazorpayWebhookSignature(rawRequestBody, webhookSecret);
            if (!SecureEquals(expectedWebhookSignature, signature))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid webhook signature."
                });
            }

            string eventType;
            string webhookPaymentId;
            string webhookOrderId;
            string webhookCurrency;
            string webhookStatus;
            long webhookAmountMinor;

            if (!TryParseRazorpayWebhookPaymentCaptured(rawRequestBody,
                out eventType,
                out webhookPaymentId,
                out webhookOrderId,
                out webhookAmountMinor,
                out webhookCurrency,
                out webhookStatus))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid webhook payload."
                });
            }

            if (!string.Equals(eventType, "payment.captured", StringComparison.OrdinalIgnoreCase))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "Webhook event ignored."
                });
            }

            var tenantName = integration.TenantName?.Trim();
            var storeCode = integration.StoreCode?.Trim();
            var transaction = await _masterDbContext.PaymentTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantName == tenantName
                                          && x.StoreCode == storeCode
                                          && x.GatewayOrderId == webhookOrderId);

            if (transaction == null)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "No matching payment transaction found for webhook order."
                });
            }

            var transactionCurrencyCode = transaction.Currency?.Trim()?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(transactionCurrencyCode))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Saved payment transaction has no currency information."
                });
            }

            var configuredCurrencyDecimalDigits = GetCurrencyDecimalDigitsForCurrencyCode(transactionCurrencyCode);
            if (!TryConvertToMinorUnits(transaction.AmountRequested, configuredCurrencyDecimalDigits, out var expectedAmountInMinorUnits)
                || expectedAmountInMinorUnits <= 0)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Saved payment amount is invalid for the configured currency."
                });
            }

            if (!string.Equals(webhookCurrency?.Trim(), transactionCurrencyCode, StringComparison.OrdinalIgnoreCase)
                || webhookAmountMinor != expectedAmountInMinorUnits
                || !string.Equals(webhookPaymentId?.Trim(), webhookPaymentId, StringComparison.Ordinal)
                || !string.Equals(webhookOrderId, transaction.GatewayOrderId, StringComparison.Ordinal)
                || !string.Equals(webhookStatus, "captured", StringComparison.OrdinalIgnoreCase))
            {
                await MarkTransactionFailedAsync(transaction.Id, "WebhookMismatch", "Webhook payment details do not match the saved transaction.");
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Webhook payment details did not match the saved transaction."
                });
            }

            string Secret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var isTestMode = integration.IsTestMode;
            var keyId = isTestMode
                ? Secret("TestKeyId", "Username")
                : Secret("LiveKeyId", "LiveApiKey", "ApiKey", "Username", "Token", "KeyId");
            var keySecret = isTestMode
                ? Secret("TestKeySecret", "Password")
                : Secret("LiveKeySecret", "LiveSecretKey", "SecretKey", "Password", "Secret", "KeySecret");

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = isTestMode
                        ? "Razorpay test mode requires TestKeyId and TestKeySecret."
                        : "Razorpay credentials are missing for the selected mode."
                });
            }

            var (paymentVerified, definitiveGatewayFailure, paymentStatusCode, paymentError, paymentDetails) = await FetchRazorpayPaymentDetailsAsync(keyId, keySecret, webhookPaymentId);
            if (!paymentVerified)
            {
                if (definitiveGatewayFailure)
                {
                    await MarkTransactionFailedAsync(transaction.Id, "GatewayVerifyFailed", paymentError ?? "Unable to verify payment from gateway.");
                }

                return ActionSet.ActionReturnType(paymentStatusCode, new CustomerIOResponse
                {
                    StatusCode = ((int)paymentStatusCode).ToString(CultureInfo.InvariantCulture),
                    Message = paymentError ?? "Unable to verify payment from gateway."
                });
            }

            if (!string.Equals(paymentDetails.PaymentId, webhookPaymentId, StringComparison.Ordinal)
                || !string.Equals(paymentDetails.OrderId, transaction.GatewayOrderId, StringComparison.Ordinal)
                || !string.Equals(paymentDetails.Currency?.Trim(), transactionCurrencyCode, StringComparison.OrdinalIgnoreCase)
                || paymentDetails.AmountInMinorUnits != expectedAmountInMinorUnits
                || !string.Equals(paymentDetails.Status, "captured", StringComparison.OrdinalIgnoreCase))
            {
                await MarkTransactionFailedAsync(transaction.Id, "GatewayMismatch", "Gateway payment data does not match the saved transaction.");
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Gateway payment details did not match the saved transaction."
                });
            }

            if (!TryConvertFromMinorUnits(paymentDetails.AmountInMinorUnits, configuredCurrencyDecimalDigits, out var capturedAmount))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Captured payment amount format is invalid for configured currency."
                });
            }

            var finalizeResult = await FinalizeCapturedPaymentAsync(
                tenantName,
                storeCode,
                transaction.Id,
                transaction.GatewayOrderId,
                webhookPaymentId,
                signature,
                capturedAmount,
                isTestMode);

            if (finalizeResult.StatusCode == HttpStatusCode.OK)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                {
                    StatusCode = "200",
                    Message = "Webhook processed successfully."
                });
            }

            return finalizeResult;
        }

        private async Task<ActionReturnType> FinalizeCapturedPaymentAsync(
            string tenantName,
            string storeCode,
            int transactionId,
            string razorpayOrderId,
            string razorpayPaymentId,
            string gatewaySignature,
            decimal capturedAmount,
            bool isTestMode)
        {
            await using var dbTransaction = await _masterDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var transaction = await _masterDbContext.PaymentTransactions
                    .FirstOrDefaultAsync(x => x.Id == transactionId
                                              && x.TenantName == tenantName
                                              && x.StoreCode == storeCode);

                if (transaction == null)
                {
                    await dbTransaction.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                    {
                        StatusCode = "404",
                        Message = "Payment transaction not found for this store."
                    });
                }

                if (!string.Equals(transaction.GatewayOrderId, razorpayOrderId, StringComparison.Ordinal))
                {
                    await dbTransaction.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                    {
                        StatusCode = "400",
                        Message = "Invalid order reference for payment confirmation."
                    });
                }

                if (string.Equals(transaction.Status, "Captured", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(transaction.GatewayPaymentId)
                        && !string.Equals(transaction.GatewayPaymentId, razorpayPaymentId, StringComparison.Ordinal))
                    {
                        await dbTransaction.RollbackAsync();
                        return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                        {
                            StatusCode = "409",
                            Message = "A different payment is already captured for this transaction."
                        });
                    }

                    await dbTransaction.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, new CustomerIOResponse
                    {
                        StatusCode = "200",
                        Message = "Payment already processed."
                    });
                }

                if (!string.IsNullOrWhiteSpace(transaction.GatewayPaymentId)
                    && !string.Equals(transaction.GatewayPaymentId, razorpayPaymentId, StringComparison.Ordinal))
                {
                    await dbTransaction.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                    {
                        StatusCode = "409",
                        Message = "Payment transaction is already linked to another payment reference."
                    });
                }

                var order = await _masterDbContext.LaundryOrders.FirstOrDefaultAsync(x =>
                    x.TenantName == tenantName
                    && x.StoreCode == storeCode
                    && x.OrderNo == transaction.OrderNo);

                if (order == null)
                {
                    await dbTransaction.RollbackAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                    {
                        StatusCode = "404",
                        Message = "Order not found for this store."
                    });
                }

                if (order.NetPayable <= 0)
                {
                    transaction.Status = "Captured";
                    transaction.GatewayPaymentId = razorpayPaymentId;
                    transaction.GatewaySignature = gatewaySignature;
                    transaction.FailureCode = null;
                    transaction.FailureReason = null;
                    transaction.AmountAuthorized = capturedAmount;
                    transaction.AmountCaptured = capturedAmount;
                    transaction.IsLiveMode = !isTestMode;
                    transaction.ModifiedDate = DateTime.UtcNow;

                    await _masterDbContext.SaveChangesAsync();
                    await dbTransaction.CommitAsync();
                    return ActionSet.ActionReturnType(HttpStatusCode.OK, MapLaundryOrderDto(order));
                }

                var payableBefore = Math.Max(0, order.NetPayable);
                var applied = Math.Min(Math.Round(capturedAmount, 2, MidpointRounding.AwayFromZero), payableBefore);

                transaction.Status = "Captured";
                transaction.GatewayPaymentId = razorpayPaymentId;
                transaction.GatewaySignature = gatewaySignature;
                transaction.FailureCode = null;
                transaction.FailureReason = null;
                transaction.AmountAuthorized = capturedAmount;
                transaction.AmountCaptured = capturedAmount;
                transaction.IsLiveMode = !isTestMode;
                transaction.ModifiedDate = DateTime.UtcNow;

                order.PaidNow = Math.Round(order.PaidNow + applied, 2, MidpointRounding.AwayFromZero);
                order.NetPayable = Math.Round(Math.Max(0, payableBefore - applied), 2, MidpointRounding.AwayFromZero);
                order.PendingAmount = order.NetPayable;

                if (string.IsNullOrWhiteSpace(order.PaymentMode))
                {
                    order.PaymentMode = "Online";
                }

                var notePrefix = $"Payment captured {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC: {applied:0.00}";
                order.Notes = string.IsNullOrWhiteSpace(order.Notes)
                    ? notePrefix
                    : (order.Notes + " || " + notePrefix);

                await _masterDbContext.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                return ActionSet.ActionReturnType(HttpStatusCode.OK, MapLaundryOrderDto(order));
            }
            catch (DbUpdateConcurrencyException)
            {
                await dbTransaction.RollbackAsync();
                return ActionSet.ActionReturnType(HttpStatusCode.Conflict, new CustomerIOResponse
                {
                    StatusCode = "409",
                    Message = "Payment confirmation conflict detected. Please retry."
                });
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                return ActionSet.ActionReturnType(HttpStatusCode.InternalServerError, new CustomerIOResponse
                {
                    StatusCode = "500",
                    Message = "Unable to confirm payment at this time."
                });
            }
        }

        private async Task<(bool Success, TenantIntegrationSettingsEntity Integration, IntegrationSettingsPayload Payload, string WebhookSecret, string ErrorMessage)> ResolveRazorpayWebhookIntegrationAsync(string provider, string referenceToken)
        {
            var normalizedProvider = NormalizeKey(provider).ToLowerInvariant();
            var normalizedReferenceToken = NormalizeKey(referenceToken).ToLowerInvariant();

            if (!string.Equals(normalizedProvider, "razorpay", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(normalizedReferenceToken))
            {
                return (false, null, null, null, "Invalid webhook context.");
            }

            var integrationRecords = await _masterDbContext.TenantIntegrationSettings
                .Where(x => x.Category == IntegrationConstants.Category.Payments && x.IsEnabled)
                .ToListAsync();

            foreach (var record in integrationRecords)
            {
                var payload = ReadPayload(record.EncryptedSettingsJson);
                if (!IsRazorpayProvider(record.Category, record.ProviderKey, record.ProviderDisplayName, payload.Public))
                {
                    continue;
                }

                var expectedReference = BuildRazorpayWebhookReferenceToken(record.TenantName, record.StoreCode, record.ProviderKey);
                if (!SecureEquals(expectedReference, normalizedReferenceToken))
                {
                    continue;
                }

                payload ??= new IntegrationSettingsPayload();
                payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!payload.Secret.TryGetValue("WebhookSecret", out var webhookSecret)
                    || string.IsNullOrWhiteSpace(webhookSecret))
                {
                    return (false, null, null, null, "Webhook secret is not configured for this Razorpay integration.");
                }

                return (true, record, payload, webhookSecret.Trim(), null);
            }

            return (false, null, null, null, "No matching Razorpay integration found for webhook context.");
        }

        private static string BuildRazorpayWebhookReferenceToken(string tenantName, string storeCode, string providerKey)
        {
            var keyMaterial = string.Join("|",
                NormalizeKey(tenantName).ToLowerInvariant(),
                NormalizeKey(storeCode).ToLowerInvariant(),
                NormalizeKey(providerKey).ToLowerInvariant(),
                "razorpay");

            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static string ComputeRazorpayWebhookSignature(string rawRequestBody, string webhookSecret)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(rawRequestBody ?? string.Empty);
            var secretBytes = Encoding.UTF8.GetBytes(webhookSecret ?? string.Empty);
            using var hmac = new HMACSHA256(secretBytes);
            var hash = hmac.ComputeHash(bodyBytes);

            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", b);
            }

            return builder.ToString();
        }

        private static bool TryParseRazorpayWebhookPaymentCaptured(
            string rawRequestBody,
            out string eventType,
            out string paymentId,
            out string orderId,
            out long amountInMinorUnits,
            out string currency,
            out string status)
        {
            eventType = null;
            paymentId = null;
            orderId = null;
            amountInMinorUnits = 0;
            currency = null;
            status = null;

            if (string.IsNullOrWhiteSpace(rawRequestBody))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(rawRequestBody);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                eventType = root.TryGetProperty("event", out var eventEl) ? eventEl.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    return false;
                }

                if (!string.Equals(eventType, "payment.captured", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !payload.TryGetProperty("payment", out var payment)
                    || payment.ValueKind != JsonValueKind.Object
                    || !payment.TryGetProperty("entity", out var entity)
                    || entity.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                paymentId = entity.TryGetProperty("id", out var idEl) ? idEl.GetString()?.Trim() : null;
                orderId = entity.TryGetProperty("order_id", out var orderEl) ? orderEl.GetString()?.Trim() : null;
                currency = entity.TryGetProperty("currency", out var currencyEl) ? currencyEl.GetString()?.Trim() : null;
                status = entity.TryGetProperty("status", out var statusEl) ? statusEl.GetString()?.Trim() : null;

                if (!entity.TryGetProperty("amount", out var amountEl)
                    || amountEl.ValueKind != JsonValueKind.Number
                    || !amountEl.TryGetInt64(out amountInMinorUnits))
                {
                    return false;
                }

                return !string.IsNullOrWhiteSpace(paymentId)
                       && !string.IsNullOrWhiteSpace(orderId)
                       && !string.IsNullOrWhiteSpace(currency)
                       && !string.IsNullOrWhiteSpace(status)
                       && amountInMinorUnits > 0;
            }
            catch
            {
                return false;
            }
        }

        private static (string ErrorCode, string ErrorMessage) ParseRazorpayError(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return (null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return (null, null);
                }

                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object)
                {
                    var code = error.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
                    var message = error.TryGetProperty("description", out var descEl)
                        ? descEl.GetString()
                        : (error.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null);
                    return (code, message);
                }
            }
            catch
            {
            }

            return (null, null);
        }

        private async Task<(bool Success, string ErrorMessage, StoreCurrencyConfiguration Currency)> ResolveStoreCurrencyConfigurationAsync(string tenantName, string storeCode)
        {
            var identityApiBaseUrl = _configuration?["IntegrationSettings:IdentityApiBaseUrl"]
                                     ?? _configuration?["ServiceEndpoints:Identity:BaseUrl"]
                                     ?? _configuration?["Identity:BaseUrl"];

            if (string.IsNullOrWhiteSpace(identityApiBaseUrl)
                || !Uri.TryCreate(identityApiBaseUrl.Trim(), UriKind.Absolute, out var identityBaseUri)
                || identityBaseUri.Scheme != Uri.UriSchemeHttps)
            {
                return (false, "Identity API base URL is not configured for currency resolution.", null);
            }

            var (storeSuccess, storeNotFound, storeError, storeCurrency) = await FetchStoreCurrencyConfigurationAsync(identityBaseUri, tenantName, storeCode);
            if (!storeSuccess && !storeNotFound)
            {
                return (false, storeError, null);
            }

            var needsFallback = storeCurrency == null
                                || string.IsNullOrWhiteSpace(storeCurrency.CurrencyCode)
                                || !storeCurrency.CurrencyDecimalDigits.HasValue;

            StoreCurrencyConfiguration tenantCurrency = null;
            if (needsFallback)
            {
                var (tenantSuccess, tenantNotFound, tenantError, tenantLevelCurrency) = await FetchStoreCurrencyConfigurationAsync(identityBaseUri, tenantName, string.Empty);
                if (!tenantSuccess && !tenantNotFound)
                {
                    return (false, tenantError, null);
                }

                tenantCurrency = tenantLevelCurrency;
            }

            var merged = MergeCurrencyConfiguration(storeCurrency, tenantCurrency);
            if (merged == null || string.IsNullOrWhiteSpace(merged.CurrencyCode))
            {
                return (false, "Currency code is not configured for this tenant/store.", null);
            }

            if (!merged.CurrencyDecimalDigits.HasValue)
            {
                return (false, "Currency decimal digits are not configured for this tenant/store.", null);
            }

            merged.CurrencyCode = merged.CurrencyCode.Trim().ToUpperInvariant();
            if (!RazorpaySupportedCurrencies.Contains(merged.CurrencyCode))
            {
                return (false, $"Configured currency '{merged.CurrencyCode}' is not supported for Razorpay orders.", null);
            }

            if (merged.CurrencyDecimalDigits.Value < 0 || merged.CurrencyDecimalDigits.Value > 6)
            {
                return (false, "Currency decimal digits must be between 0 and 6.", null);
            }

            return (true, null, merged);
        }

        private async Task<(bool Success, bool NotFound, string ErrorMessage, StoreCurrencyConfiguration Currency)> FetchStoreCurrencyConfigurationAsync(Uri identityBaseUri, string tenantName, string storeCode)
        {
            var endpoint = new Uri(identityBaseUri, $"/Login/GetStoreConfiguration?tenantEmail={Uri.EscapeDataString(tenantName ?? string.Empty)}&storeCode={Uri.EscapeDataString(storeCode ?? string.Empty)}");

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            try
            {
                using var response = await httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return (false, true, null, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return (false, false, ExtractMessageFromJson(responseBody) ?? "Unable to load store currency configuration.", null);
                }

                var currency = ExtractCurrencyConfigurationFromResponse(responseBody);
                return (true, false, null, currency);
            }
            catch (TaskCanceledException)
            {
                return (false, false, "Currency configuration request timed out.", null);
            }
            catch (HttpRequestException)
            {
                return (false, false, "Unable to reach identity service for currency configuration.", null);
            }
        }

        private static StoreCurrencyConfiguration MergeCurrencyConfiguration(StoreCurrencyConfiguration primary, StoreCurrencyConfiguration fallback)
        {
            if (primary == null && fallback == null)
            {
                return null;
            }

            return new StoreCurrencyConfiguration
            {
                CurrencyCode = !string.IsNullOrWhiteSpace(primary?.CurrencyCode)
                    ? primary.CurrencyCode
                    : fallback?.CurrencyCode,
                CurrencySymbol = !string.IsNullOrWhiteSpace(primary?.CurrencySymbol)
                    ? primary.CurrencySymbol
                    : fallback?.CurrencySymbol,
                CurrencyLocale = !string.IsNullOrWhiteSpace(primary?.CurrencyLocale)
                    ? primary.CurrencyLocale
                    : fallback?.CurrencyLocale,
                CurrencyDecimalDigits = primary?.CurrencyDecimalDigits ?? fallback?.CurrencyDecimalDigits
            };
        }

        private async Task<(bool Success, string KeyId, string KeySecret, bool IsTestMode, string ErrorMessage)> ResolveRazorpayCredentialsAsync(string tenantName, string storeCode)
        {
            var integrationRecords = await _masterDbContext.TenantIntegrationSettings
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.Category == IntegrationConstants.Category.Payments
                            && x.IsEnabled)
                .ToListAsync();

            TenantIntegrationSettingsEntity razorpayIntegration = null;
            IntegrationSettingsPayload integrationPayload = null;
            foreach (var record in integrationRecords)
            {
                var payload = ReadPayload(record.EncryptedSettingsJson);
                if (IsRazorpayProvider(record.Category, record.ProviderKey, record.ProviderDisplayName, payload.Public))
                {
                    razorpayIntegration = record;
                    integrationPayload = payload;
                    break;
                }
            }

            if (razorpayIntegration == null)
            {
                return (false, null, null, false, "Enabled Razorpay configuration not found for this store.");
            }

            integrationPayload ??= new IntegrationSettingsPayload();
            integrationPayload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Secret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (integrationPayload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var isTestMode = razorpayIntegration.IsTestMode;
            var keyId = isTestMode
                ? Secret("TestKeyId", "Username")
                : Secret("LiveKeyId", "LiveApiKey", "ApiKey", "Username", "Token", "KeyId");
            var keySecret = isTestMode
                ? Secret("TestKeySecret", "Password")
                : Secret("LiveKeySecret", "LiveSecretKey", "SecretKey", "Password", "Secret", "KeySecret");

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                return (false, null, null, isTestMode, isTestMode
                    ? "Razorpay test mode requires TestKeyId and TestKeySecret."
                    : "Razorpay credentials are missing for the selected mode.");
            }

            return (true, keyId.Trim(), keySecret.Trim(), isTestMode, null);
        }

        private static string ComputeRazorpayCheckoutSignature(string razorpayOrderId, string razorpayPaymentId, string keySecret)
        {
            var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
            var keyBytes = Encoding.UTF8.GetBytes(keySecret ?? string.Empty);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);

            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", b);
            }

            return builder.ToString();
        }

        private static bool SecureEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var normalizedLeft = left.Trim().ToLowerInvariant();
            var normalizedRight = right.Trim().ToLowerInvariant();

            var leftBytes = Encoding.UTF8.GetBytes(normalizedLeft);
            var rightBytes = Encoding.UTF8.GetBytes(normalizedRight);

            if (leftBytes.Length != rightBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private async Task<(bool Success, bool DefinitiveFailure, HttpStatusCode StatusCode, string ErrorMessage, RazorpayPaymentVerificationDetails Payment)> FetchRazorpayPaymentDetailsAsync(string keyId, string keySecret, string razorpayPaymentId)
        {
            var razorpayApiBaseUrl = _configuration?["IntegrationSettings:RazorpayApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(razorpayApiBaseUrl))
            {
                razorpayApiBaseUrl = "https://api.razorpay.com";
            }

            if (!Uri.TryCreate(razorpayApiBaseUrl.Trim(), UriKind.Absolute, out var razorpayBaseUri)
                || razorpayBaseUri.Scheme != Uri.UriSchemeHttps)
            {
                return (false, true, HttpStatusCode.BadRequest, "Razorpay API base URL is invalid.", null);
            }

            var encodedPaymentId = Uri.EscapeDataString(razorpayPaymentId ?? string.Empty);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(razorpayBaseUri, $"/v1/payments/{encodedPaymentId}"));
            var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                using var httpResponse = await httpClient.SendAsync(httpRequest);
                var responseBody = await httpResponse.Content.ReadAsStringAsync();
                if (!httpResponse.IsSuccessStatusCode)
                {
                    var (_, errorMessage) = ParseRazorpayError(responseBody);
                    var isDefinitive = (int)httpResponse.StatusCode >= 400 && (int)httpResponse.StatusCode < 500;
                    var mappedStatus = isDefinitive ? HttpStatusCode.BadRequest : HttpStatusCode.BadGateway;
                    return (false, isDefinitive, mappedStatus,
                        string.IsNullOrWhiteSpace(errorMessage) ? "Gateway verification failed." : errorMessage,
                        null);
                }

                using var json = JsonDocument.Parse(responseBody);
                var root = json.RootElement;

                var paymentId = root.TryGetProperty("id", out var idEl) ? idEl.GetString()?.Trim() : null;
                var orderId = root.TryGetProperty("order_id", out var orderEl) ? orderEl.GetString()?.Trim() : null;
                var currency = root.TryGetProperty("currency", out var currencyEl) ? currencyEl.GetString()?.Trim() : null;
                var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString()?.Trim() : null;

                long amountMinor;
                if (!root.TryGetProperty("amount", out var amountEl)
                    || amountEl.ValueKind != JsonValueKind.Number
                    || !amountEl.TryGetInt64(out amountMinor))
                {
                    return (false, true, HttpStatusCode.BadGateway, "Invalid gateway payment amount response.", null);
                }

                if (string.IsNullOrWhiteSpace(paymentId)
                    || string.IsNullOrWhiteSpace(orderId)
                    || string.IsNullOrWhiteSpace(currency)
                    || string.IsNullOrWhiteSpace(status))
                {
                    return (false, true, HttpStatusCode.BadGateway, "Invalid gateway payment verification response.", null);
                }

                return (true, false, HttpStatusCode.OK, null, new RazorpayPaymentVerificationDetails
                {
                    PaymentId = paymentId,
                    OrderId = orderId,
                    AmountInMinorUnits = amountMinor,
                    Currency = currency,
                    Status = status
                });
            }
            catch (TaskCanceledException)
            {
                return (false, false, HttpStatusCode.BadGateway, "Razorpay payment verification timed out. Please retry.", null);
            }
            catch (HttpRequestException)
            {
                return (false, false, HttpStatusCode.BadGateway, "Unable to reach Razorpay API. Please retry.", null);
            }
            catch
            {
                return (false, false, HttpStatusCode.BadGateway, "Unable to verify payment from gateway.", null);
            }
        }

        private async Task MarkTransactionFailedAsync(int transactionId, string failureCode, string failureReason)
        {
            var tx = await _masterDbContext.PaymentTransactions.FirstOrDefaultAsync(x => x.Id == transactionId);
            if (tx == null || string.Equals(tx.Status, "Captured", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            tx.Status = "Failed";
            tx.FailureCode = failureCode;
            tx.FailureReason = failureReason;
            tx.ModifiedDate = DateTime.UtcNow;
            await _masterDbContext.SaveChangesAsync();
        }

        private static bool TryConvertFromMinorUnits(long minorUnits, int decimalDigits, out decimal majorUnits)
        {
            majorUnits = 0;
            if (minorUnits < 0 || decimalDigits < 0 || decimalDigits > 6)
            {
                return false;
            }

            decimal factor = 1m;
            for (var i = 0; i < decimalDigits; i++)
            {
                factor *= 10m;
            }

            if (factor <= 0)
            {
                return false;
            }

            majorUnits = minorUnits / factor;
            return majorUnits >= 0;
        }

        private static int GetCurrencyDecimalDigitsForCurrencyCode(string currencyCode)
        {
            if (string.IsNullOrWhiteSpace(currencyCode))
            {
                return 2;
            }

            var normalized = currencyCode.Trim().ToUpperInvariant();

            if (normalized == "BHD" || normalized == "KWD" || normalized == "OMR")
            {
                return 3;
            }

            if (normalized == "JPY" || normalized == "KRW" || normalized == "VND")
            {
                return 0;
            }

            return 2;
        }

        private static LaundryOrderDto MapLaundryOrderDto(LaundryOrderEntity order)
        {
            if (order == null)
            {
                return null;
            }

            return new LaundryOrderDto
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
            };
        }

        private static StoreCurrencyConfiguration ExtractCurrencyConfigurationFromResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var currency = new StoreCurrencyConfiguration();
                if (TryFindStringProperty(doc.RootElement, "ConfigurationJson", out var configurationJson)
                    && !string.IsNullOrWhiteSpace(configurationJson))
                {
                    try
                    {
                        using var configDoc = JsonDocument.Parse(configurationJson);
                        currency = ExtractCurrencyConfigurationFromElement(configDoc.RootElement);
                    }
                    catch
                    {
                        currency = new StoreCurrencyConfiguration();
                    }
                }

                var direct = ExtractCurrencyConfigurationFromElement(doc.RootElement);
                return MergeCurrencyConfiguration(currency, direct);
            }
            catch
            {
                return null;
            }
        }

        private static StoreCurrencyConfiguration ExtractCurrencyConfigurationFromElement(JsonElement element)
        {
            var result = new StoreCurrencyConfiguration();

            if (TryFindStringProperty(element, "CurrencyCode", out var code))
            {
                result.CurrencyCode = code?.Trim();
            }

            if (TryFindStringProperty(element, "CurrencySymbol", out var symbol))
            {
                result.CurrencySymbol = symbol?.Trim();
            }

            if (TryFindStringProperty(element, "CurrencyLocale", out var locale))
            {
                result.CurrencyLocale = locale?.Trim();
            }

            if (TryFindIntProperty(element, "CurrencyDecimalDigits", out var digits))
            {
                result.CurrencyDecimalDigits = digits;
            }

            return result;
        }

        private static bool TryFindStringProperty(JsonElement element, string propertyName, out string value)
        {
            value = null;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        value = property.Value.GetString();
                        return true;
                    }

                    if (TryFindStringProperty(property.Value, propertyName, out value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindStringProperty(item, propertyName, out value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryFindIntProperty(JsonElement element, string propertyName, out int value)
        {
            value = 0;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out value))
                        {
                            return true;
                        }

                        if (property.Value.ValueKind == JsonValueKind.String
                            && int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        {
                            return true;
                        }
                    }

                    if (TryFindIntProperty(property.Value, propertyName, out value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindIntProperty(item, propertyName, out value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryConvertToMinorUnits(decimal amount, int decimalDigits, out long minorUnits)
        {
            minorUnits = 0;
            if (amount <= 0 || decimalDigits < 0 || decimalDigits > 6)
            {
                return false;
            }

            decimal factor = 1m;
            for (var i = 0; i < decimalDigits; i++)
            {
                factor *= 10m;
            }

            var scaled = amount * factor;
            if (decimal.Truncate(scaled) != scaled)
            {
                return false;
            }

            if (scaled > long.MaxValue)
            {
                return false;
            }

            minorUnits = Convert.ToInt64(scaled, CultureInfo.InvariantCulture);
            return minorUnits > 0;
        }

        private static string ExtractMessageFromJson(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (TryFindStringProperty(doc.RootElement, "Message", out var message)
                    && !string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
            catch
            {
            }

            return null;
        }

        public async Task<ActionReturnType> SettleLaundryOrderPayment(string tenantName, string storeCode, string orderNo, decimal paidAmount, string paymentMode = null, string notes = null)
        {
            await EnsureLaundryOrdersTableExistsAsync();
            await EnsurePaymentTransactionsTableExistsAsync();

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

            await PopulateOrderItemsAndTotalsAsync(tenantName, storeCode, orders);

            foreach (var order in orders)
            {
                if (TryNormalizeWorkflowLifecycleStatus(order.Status, out var normalized))
                {
                    order.Status = normalized;
                }
            }

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderResponseDto
            {
                StatusCode = "200",
                CustCode = custCode,
                Orders = orders
            });
        }

        public async Task<ActionReturnType> GetLaundryOrderTaxLines(string tenantName, string storeCode, string orderNo)
        {
            if (string.IsNullOrWhiteSpace(tenantName)
                || string.IsNullOrWhiteSpace(storeCode)
                || string.IsNullOrWhiteSpace(orderNo))
            {
                return ActionSet.ActionReturnType(HttpStatusCode.BadRequest, new CustomerIOResponse
                {
                    StatusCode = "400",
                    Message = "Invalid order tax lines request."
                });
            }

            tenantName = tenantName.Trim();
            storeCode = storeCode.Trim();
            orderNo = orderNo.Trim();

            var orderExists = await _masterDbContext.LaundryOrders
                .AsNoTracking()
                .AnyAsync(x => x.TenantName == tenantName
                               && x.StoreCode == storeCode
                               && x.OrderNo == orderNo);

            if (!orderExists)
            {
                return ActionSet.ActionReturnType(HttpStatusCode.NotFound, new CustomerIOResponse
                {
                    StatusCode = "404",
                    Message = "Order not found for selected store."
                });
            }

            var lines = await _masterDbContext.LaundryOrderTaxLines
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName
                            && x.StoreCode == storeCode
                            && x.OrderNo == orderNo)
                .OrderBy(x => x.LineNo)
                .ThenBy(x => x.Priority)
                .ThenBy(x => x.ComponentCode)
                .Select(x => new LaundryOrderTaxLineDto
                {
                    LineNo = x.LineNo,
                    TaxMode = x.TaxMode,
                    CurrencyCode = x.CurrencyCode,
                    ComponentCode = x.ComponentCode,
                    ComponentDisplayName = x.ComponentDisplayName,
                    Jurisdiction = x.Jurisdiction,
                    Priority = x.Priority,
                    IsCompound = x.IsCompound,
                    RatePercent = x.RatePercent,
                    TaxableBaseAmount = x.TaxableBaseAmount,
                    TaxAmount = x.TaxAmount
                })
                .ToListAsync();

            return ActionSet.ActionReturnType(HttpStatusCode.OK, new LaundryOrderTaxLinesResponseDto
            {
                StatusCode = "200",
                OrderNo = orderNo,
                TaxLines = lines
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

            await PopulateOrderItemsAndTotalsAsync(tenantName, storeCode, orders);

            foreach (var order in orders)
            {
                if (TryNormalizeWorkflowLifecycleStatus(order.Status, out var normalized))
                {
                    order.Status = normalized;
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

            var matches = Regex.Matches(
                notes,
                @"\bx\s*(\d+)\b",
                RegexOptions.IgnoreCase);

            var total = 0;
            foreach (Match match in matches)
            {
                if (match.Success && int.TryParse(match.Groups[1].Value, out var quantity) && quantity > 0)
                {
                    total += quantity;
                }
            }

            return total;
        }

        private static List<LaundryOrderItemDto> NormalizeSubmittedOrderItems(IEnumerable<LaundryOrderItemDto> items)
        {
            return (items ?? Enumerable.Empty<LaundryOrderItemDto>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ItemName) && x.Quantity > 0)
                .Select(x => new LaundryOrderItemDto
                {
                    ServiceType = string.IsNullOrWhiteSpace(x.ServiceType) ? null : x.ServiceType.Trim(),
                    Category = string.IsNullOrWhiteSpace(x.Category) ? null : x.Category.Trim(),
                    ItemName = x.ItemName.Trim(),
                    UnitPrice = x.UnitPrice,
                    Quantity = x.Quantity > 0 ? x.Quantity : 1
                })
                .ToList();
        }

        private async Task PopulateOrderItemsAndTotalsAsync(string tenantName, string storeCode, List<LaundryOrderDto> orders)
        {
            if (orders == null || orders.Count == 0)
            {
                return;
            }

            var orderNos = orders
                .Select(x => x?.OrderNo?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderNos.Count == 0)
            {
                foreach (var order in orders)
                {
                    order.Items = new List<LaundryOrderItemDto>();
                    order.TotalPieces = string.Equals(order.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase)
                        ? ExtractTotalPieces(order.Notes)
                        : 0;
                }

                return;
            }

            var persistedItems = new List<(string OrderNo, LaundryOrderItemDto Item)>();
            try
            {
                var persistedRows = await _masterDbContext.LaundryOrderItems
                    .AsNoTracking()
                    .Where(x => x.TenantName == tenantName
                                && x.StoreCode == storeCode
                                && orderNos.Contains(x.OrderNo))
                    .OrderBy(x => x.Id)
                    .Select(x => new
                    {
                        x.OrderNo,
                        x.ServiceType,
                        x.Category,
                        x.ItemName,
                        x.UnitPrice,
                        x.PieceNo,
                        x.TagNo
                    })
                    .ToListAsync();

                persistedItems = persistedRows
                    .Select(x => (x.OrderNo, new LaundryOrderItemDto
                    {
                        ServiceType = x.ServiceType,
                        Category = x.Category,
                        ItemName = x.ItemName,
                        UnitPrice = x.UnitPrice,
                        Quantity = 1,
                        PieceNo = x.PieceNo,
                        TagNo = x.TagNo
                    }))
                    .ToList();
            }
            catch
            {
            }

            var grouped = persistedItems
                .GroupBy(x => x.OrderNo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Item).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var order in orders)
            {
                var orderNo = order?.OrderNo?.Trim();
                if (!string.IsNullOrWhiteSpace(orderNo) && grouped.TryGetValue(orderNo, out var itemsForOrder) && itemsForOrder.Count > 0)
                {
                    order.Items = itemsForOrder;
                    order.TotalPieces = itemsForOrder.Count;
                }
                else
                {
                    order.Items = new List<LaundryOrderItemDto>();
                    order.TotalPieces = string.Equals(order.OrderMode, "pieces", StringComparison.OrdinalIgnoreCase)
                        ? ExtractTotalPieces(order.Notes)
                        : 0;
                }
            }
        }

        private static string BuildPrintableTagNumber(string tagPrefix, int nextTagNumber, int tagNumberPadding)
        {
            var safeNumber = nextTagNumber < 1 ? 1 : nextTagNumber;
            var safePadding = tagNumberPadding < 1 || tagNumberPadding > 12 ? 4 : tagNumberPadding;
            var numberPart = safeNumber.ToString(CultureInfo.InvariantCulture).PadLeft(safePadding, '0');
            var prefix = (tagPrefix ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(prefix)
                ? numberPart
                : prefix + numberPart;
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

            await EnsureLaundryItemPriceDefaultsInitializedAsync(tenantName, storeCode);

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

        private async Task EnsureLaundryItemPriceDefaultsInitializedAsync(string tenantName, string storeCode)
        {
            var hasAnyConfiguredItems = await _masterDbContext.LaundryItemPrices
                .AsNoTracking()
                .AnyAsync(x => x.TenantName == tenantName && x.StoreCode == storeCode);

            if (hasAnyConfiguredItems)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var defaults = BuildDefaultLaundryItemPrices(tenantName, storeCode);
            defaults.ForEach(x =>
            {
                x.CreatedDate = now;
                x.ModifiedDate = now;
                x.IsActive = true;
            });

            if (defaults.Count == 0)
            {
                return;
            }

            _masterDbContext.LaundryItemPrices.AddRange(defaults);
            await _masterDbContext.SaveChangesAsync();
        }

        private async Task EnsureStoreMasterDefaultsInitializedAsync(string tenantName, string storeCode)
        {
            await EnsureLaundryItemPricesTableExistsAsync();
            await EnsureLaundryItemPriceDefaultsInitializedAsync(tenantName, storeCode);

            var sourceItems = await _masterDbContext.LaundryItemPrices
                .AsNoTracking()
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .Select(x => new
                {
                    ServiceType = x.ServiceType,
                    Category = x.Category,
                    ItemName = x.ItemName,
                    x.IsActive
                })
                .ToListAsync();

            if (sourceItems.Count == 0)
            {
                return;
            }

            var normalizedItems = sourceItems
                .Where(x => !string.IsNullOrWhiteSpace(x.ServiceType)
                            && !string.IsNullOrWhiteSpace(x.Category)
                            && !string.IsNullOrWhiteSpace(x.ItemName))
                .Select(x => new
                {
                    ServiceType = x.ServiceType.Trim(),
                    Category = x.Category.Trim(),
                    ItemName = x.ItemName.Trim(),
                    x.IsActive
                })
                .ToList();

            if (normalizedItems.Count == 0)
            {
                return;
            }

            var existingServiceMasters = await _masterDbContext.StoreServiceMasters
                .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                .Select(x => new { x.MasterType, x.Name })
                .ToListAsync();

            var serviceMasterKeys = new HashSet<string>(
                existingServiceMasters
                    .Where(x => !string.IsNullOrWhiteSpace(x.MasterType)
                                && !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => $"{x.MasterType.Trim()}|{x.Name.Trim()}"),
                StringComparer.OrdinalIgnoreCase);

            var existingItemMasterKeys = new HashSet<string>(
                await _masterDbContext.StoreItemMasters
                    .Where(x => x.TenantName == tenantName && x.StoreCode == storeCode)
                    .Select(x => x.ServiceType + "|" + x.Category + "|" + x.ItemName)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var changed = false;

            foreach (var serviceType in normalizedItems
                .Select(x => x.ServiceType)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = $"Service|{serviceType}";
                if (serviceMasterKeys.Contains(key))
                {
                    continue;
                }

                _masterDbContext.StoreServiceMasters.Add(new StoreServiceMasterEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    MasterType = "Service",
                    Name = serviceType,
                    Description = null,
                    IsActive = normalizedItems.Any(x => string.Equals(x.ServiceType, serviceType, StringComparison.OrdinalIgnoreCase) && x.IsActive),
                    CreatedDate = now,
                    ModifiedDate = now
                });

                serviceMasterKeys.Add(key);
                changed = true;
            }

            foreach (var category in normalizedItems
                .Select(x => x.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = $"Category|{category}";
                if (serviceMasterKeys.Contains(key))
                {
                    continue;
                }

                _masterDbContext.StoreServiceMasters.Add(new StoreServiceMasterEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    MasterType = "Category",
                    Name = category,
                    Description = null,
                    IsActive = normalizedItems.Any(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase) && x.IsActive),
                    CreatedDate = now,
                    ModifiedDate = now
                });

                serviceMasterKeys.Add(key);
                changed = true;
            }

            foreach (var item in normalizedItems)
            {
                var key = $"{item.ServiceType}|{item.Category}|{item.ItemName}";
                if (existingItemMasterKeys.Contains(key))
                {
                    continue;
                }

                _masterDbContext.StoreItemMasters.Add(new StoreItemMasterEntity
                {
                    TenantName = tenantName,
                    StoreCode = storeCode,
                    ServiceType = item.ServiceType,
                    Category = item.Category,
                    ItemName = item.ItemName,
                    Description = null,
                    IsActive = item.IsActive,
                    CreatedDate = now,
                    ModifiedDate = now
                });

                existingItemMasterKeys.Add(key);
                changed = true;
            }

            if (changed)
            {
                await _masterDbContext.SaveChangesAsync();
            }
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

            await EnsureStoreMasterDefaultsInitializedAsync(tenantName, storeCode);

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

            await EnsureStoreMasterDefaultsInitializedAsync(tenantName, storeCode);

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
        [TaxMode] [nvarchar](24) NULL,
        [CountryCode] [nvarchar](8) NULL,
        [CurrencyCode] [nvarchar](8) NULL,
        [IsPricesInclusive] [bit] NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        [RowVersion] [rowversion] NOT NULL,
        CONSTRAINT [PK_TaxInvoiceSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'TaxMode') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [TaxMode] [nvarchar](24) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'CountryCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [CountryCode] [nvarchar](8) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'CurrencyCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [CurrencyCode] [nvarchar](8) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'IsPricesInclusive') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [IsPricesInclusive] [bit] NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'RowVersion') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [RowVersion] [rowversion] NOT NULL;
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

        private async Task EnsureTaxInvoiceComponentsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[TaxInvoiceComponents]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TaxInvoiceComponents](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TaxInvoiceSettingsId] [int] NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [Code] [nvarchar](32) NOT NULL,
        [DisplayName] [nvarchar](128) NOT NULL,
        [RatePercent] [decimal](5,2) NOT NULL,
        [Jurisdiction] [nvarchar](64) NULL,
        [JurisdictionNormalizedKey] AS (COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''),N'__NULL__')) PERSISTED,
        [Priority] [int] NOT NULL,
        [IsCompound] [bit] NOT NULL,
        [IsActive] [bit] NOT NULL,
        [EffectiveFromUtc] [datetime2](7) NULL,
        [EffectiveToUtc] [datetime2](7) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        [RowVersion] [rowversion] NOT NULL,
        CONSTRAINT [PK_TaxInvoiceComponents] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_TaxInvoiceComponents_TaxInvoiceSettings] FOREIGN KEY ([TaxInvoiceSettingsId])
            REFERENCES [dbo].[TaxInvoiceSettings]([Id]) ON DELETE CASCADE
    );
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'TaxInvoiceSettingsId') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [TaxInvoiceSettingsId] [int] NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'TenantName') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [TenantName] [nvarchar](256) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'StoreCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [StoreCode] [nvarchar](64) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Code') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [Code] [nvarchar](32) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'DisplayName') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [DisplayName] [nvarchar](128) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'RatePercent') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [RatePercent] [decimal](5,2) NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_RatePercent] DEFAULT((0));
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Jurisdiction') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [Jurisdiction] [nvarchar](64) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'JurisdictionNormalizedKey') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents]
    ADD [JurisdictionNormalizedKey] AS (COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''),N'__NULL__')) PERSISTED;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Priority') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [Priority] [int] NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_Priority] DEFAULT((1));
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'IsCompound') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [IsCompound] [bit] NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_IsCompound] DEFAULT((0));
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'IsActive') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [IsActive] [bit] NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_IsActive] DEFAULT((1));
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'EffectiveFromUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [EffectiveFromUtc] [datetime2](7) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'EffectiveToUtc') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [EffectiveToUtc] [datetime2](7) NULL;
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'CreatedDate') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [CreatedDate] [datetime2](7) NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_CreatedDate] DEFAULT (SYSUTCDATETIME());
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'ModifiedDate') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [ModifiedDate] [datetime2](7) NOT NULL CONSTRAINT [DF_TaxInvoiceComponents_ModifiedDate] DEFAULT (SYSUTCDATETIME());
END;

IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'RowVersion') IS NULL
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ADD [RowVersion] [rowversion] NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceSettings]
    GROUP BY [TenantName], [StoreCode]
    HAVING COUNT(1) > 1
)
BEGIN
    THROW 51010, 'TaxInvoiceSettings contains duplicate (TenantName, StoreCode). Resolve duplicates before TaxInvoiceComponents repair can continue.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceComponents] c
    LEFT JOIN [dbo].[TaxInvoiceSettings] s
      ON s.[TenantName] = c.[TenantName]
     AND s.[StoreCode] = c.[StoreCode]
    WHERE c.[TaxInvoiceSettingsId] IS NULL
      AND s.[Id] IS NULL
)
BEGIN
    THROW 51011, 'TaxInvoiceComponents contains rows with no matching TaxInvoiceSettings by tenant/store. Repair stopped to prevent incorrect ownership backfill.', 1;
END;

UPDATE c
SET c.[TaxInvoiceSettingsId] = s.[Id]
FROM [dbo].[TaxInvoiceComponents] c
INNER JOIN [dbo].[TaxInvoiceSettings] s
    ON s.[TenantName] = c.[TenantName]
   AND s.[StoreCode] = c.[StoreCode]
WHERE c.[TaxInvoiceSettingsId] IS NULL;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceComponents]
    WHERE [TaxInvoiceSettingsId] IS NULL
)
BEGIN
    THROW 51012, 'TaxInvoiceComponents contains NULL TaxInvoiceSettingsId after backfill. Repair stopped.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceComponents] c
    LEFT JOIN [dbo].[TaxInvoiceSettings] s
      ON s.[Id] = c.[TaxInvoiceSettingsId]
    WHERE s.[Id] IS NULL
)
BEGIN
    THROW 51013, 'TaxInvoiceComponents contains orphan TaxInvoiceSettingsId values. Repair stopped.', 1;
END;

IF COLUMNPROPERTY(OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'), N'TaxInvoiceSettingsId', 'AllowsNull') = 1
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] ALTER COLUMN [TaxInvoiceSettingsId] [int] NOT NULL;
END;

IF COLUMNPROPERTY(OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'), N'TenantName', 'AllowsNull') = 1
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [TenantName] IS NULL)
    BEGIN
        THROW 51020, 'TaxInvoiceComponents.TenantName contains NULL values. Repair stopped before enforcing NOT NULL.', 1;
    END;

    ALTER TABLE [dbo].[TaxInvoiceComponents] ALTER COLUMN [TenantName] [nvarchar](256) NOT NULL;
END;

IF COLUMNPROPERTY(OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'), N'StoreCode', 'AllowsNull') = 1
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [StoreCode] IS NULL)
    BEGIN
        THROW 51021, 'TaxInvoiceComponents.StoreCode contains NULL values. Repair stopped before enforcing NOT NULL.', 1;
    END;

    ALTER TABLE [dbo].[TaxInvoiceComponents] ALTER COLUMN [StoreCode] [nvarchar](64) NOT NULL;
END;

IF COLUMNPROPERTY(OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'), N'Code', 'AllowsNull') = 1
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [Code] IS NULL)
    BEGIN
        THROW 51022, 'TaxInvoiceComponents.Code contains NULL values. Repair stopped before enforcing NOT NULL.', 1;
    END;

    ALTER TABLE [dbo].[TaxInvoiceComponents] ALTER COLUMN [Code] [nvarchar](32) NOT NULL;
END;

IF COLUMNPROPERTY(OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'), N'DisplayName', 'AllowsNull') = 1
BEGIN
    IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [DisplayName] IS NULL)
    BEGIN
        THROW 51023, 'TaxInvoiceComponents.DisplayName contains NULL values. Repair stopped before enforcing NOT NULL.', 1;
    END;

    ALTER TABLE [dbo].[TaxInvoiceComponents] ALTER COLUMN [DisplayName] [nvarchar](128) NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_TaxInvoiceComponents_TaxInvoiceSettings'
      AND parent_object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    ALTER TABLE [dbo].[TaxInvoiceComponents] WITH CHECK ADD CONSTRAINT [FK_TaxInvoiceComponents_TaxInvoiceSettings]
    FOREIGN KEY([TaxInvoiceSettingsId]) REFERENCES [dbo].[TaxInvoiceSettings]([Id]) ON DELETE CASCADE;
END;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceComponents] c
    LEFT JOIN [dbo].[TaxInvoiceSettings] s
      ON s.[Id] = c.[TaxInvoiceSettingsId]
    WHERE s.[Id] IS NULL
)
BEGIN
    THROW 51024, 'TaxInvoiceComponents FK verification failed: orphan TaxInvoiceSettingsId rows exist.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive]
    ON [dbo].[TaxInvoiceComponents] ([TenantName], [StoreCode], [IsActive]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive]
    ON [dbo].[TaxInvoiceComponents] ([TaxInvoiceSettingsId], [Code], [IsActive]);
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceComponents_Tenant_Store_Code_Priority'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    DROP INDEX [IX_TaxInvoiceComponents_Tenant_Store_Code_Priority] ON [dbo].[TaxInvoiceComponents];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceComponents_Tenant_Store_Code_Priority_Jurisdiction_Active'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    DROP INDEX [IX_TaxInvoiceComponents_Tenant_Store_Code_Priority_Jurisdiction_Active] ON [dbo].[TaxInvoiceComponents];
END;

IF EXISTS (
    SELECT 1
    FROM [dbo].[TaxInvoiceComponents]
    WHERE [IsActive] = 1
    GROUP BY [TenantName], [StoreCode], [Code], [Priority], [JurisdictionNormalizedKey]
    HAVING COUNT(1) > 1
)
BEGIN
    THROW 51014, 'Active TaxInvoiceComponents contains duplicate code/priority/jurisdiction keys. Resolve duplicates before creating unique index.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TaxInvoiceComponents_Tenant_Store_Code_Priority_Jurisdiction_Active'
      AND object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_TaxInvoiceComponents_Tenant_Store_Code_Priority_Jurisdiction_Active]
    ON [dbo].[TaxInvoiceComponents] ([TenantName], [StoreCode], [Code], [Priority], [JurisdictionNormalizedKey])
    WHERE [IsActive] = 1;
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private async Task EnsureTenantIntegrationSettingsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[TenantIntegrationSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TenantIntegrationSettings](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [Category] [nvarchar](64) NOT NULL,
        [ProviderKey] [nvarchar](64) NOT NULL,
        [ProviderDisplayName] [nvarchar](128) NULL,
        [IsEnabled] [bit] NOT NULL,
        [IsTestMode] [bit] NOT NULL,
        [EncryptedSettingsJson] [nvarchar](max) NULL,
        [HasSecretsConfigured] [bit] NOT NULL,
        [LastTestStatus] [nvarchar](32) NULL,
        [LastTestMessage] [nvarchar](512) NULL,
        [LastTestedAtUtc] [datetime2](7) NULL,
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        CONSTRAINT [PK_TenantIntegrationSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TenantIntegrationSettings_Tenant_Store_Category_Provider'
      AND object_id = OBJECT_ID(N'[dbo].[TenantIntegrationSettings]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_TenantIntegrationSettings_Tenant_Store_Category_Provider]
    ON [dbo].[TenantIntegrationSettings] ([TenantName], [StoreCode], [Category], [ProviderKey]);
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
        }

        private TenantIntegrationSettingsDto MapToIntegrationDto(TenantIntegrationSettingsEntity entity)
        {
            var dto = _mapper.Map<TenantIntegrationSettingsDto>(entity) ?? new TenantIntegrationSettingsDto();
            var payload = ReadPayload(entity?.EncryptedSettingsJson);

            dto.PublicSettings = payload.Public ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dto.SecretSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dto.SecretKeys = payload.Secret?.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k) && !k.StartsWith("__els_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k)
                .ToList() ?? new List<string>();

            foreach (var key in dto.SecretKeys)
            {
                dto.SecretSettings[key] = "********";
            }

            if (IsRazorpayProvider(dto.Category, dto.ProviderKey, dto.ProviderDisplayName, payload?.Public)
                && TryBuildRazorpayWebhookUrl(dto.TenantName, dto.StoreCode, dto.ProviderKey, out var generatedWebhookUrl))
            {
                dto.PublicSettings["WebhookUrl"] = generatedWebhookUrl;
            }

            // For SMTP UI convenience, show saved username/API key while never exposing
            // password/token/secret values back to browser clients.
            if (string.Equals(dto.Category, IntegrationConstants.Category.Email, StringComparison.OrdinalIgnoreCase)
                && string.Equals(dto.ProviderKey, "smtp", StringComparison.OrdinalIgnoreCase))
            {
                if (payload.Secret != null
                    && payload.Secret.TryGetValue("Username", out var smtpUsername)
                    && !string.IsNullOrWhiteSpace(smtpUsername))
                {
                    dto.SecretSettings["Username"] = smtpUsername;
                }
                else if (payload.Secret != null
                    && payload.Secret.TryGetValue("ApiKey", out var smtpApiKey)
                    && !string.IsNullOrWhiteSpace(smtpApiKey))
                {
                    dto.SecretSettings["Username"] = smtpApiKey;
                }
            }

            dto.HasSecretsConfigured = entity?.HasSecretsConfigured == true;
            dto.HasSavedRecord = entity != null;
            return dto;
        }

        private IntegrationSettingsPayload ReadPayload(string encryptedPayload)
        {
            if (string.IsNullOrWhiteSpace(encryptedPayload))
            {
                return new IntegrationSettingsPayload();
            }

            static Dictionary<string, string> NormalizeSettings(Dictionary<string, string> source)
            {
                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (source == null)
                {
                    return normalized;
                }

                foreach (var item in source)
                {
                    var key = item.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    normalized[key] = item.Value?.Trim();
                }

                return normalized;
            }

            static Dictionary<string, string> ReadSettingsObject(JsonElement root, params string[] propertyNames)
            {
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                JsonElement selected = default;
                var found = false;

                foreach (var property in root.EnumerateObject())
                {
                    if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        selected = property.Value;
                        found = true;
                        break;
                    }
                }

                if (!found || selected.ValueKind != JsonValueKind.Object)
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in selected.EnumerateObject())
                {
                    var key = item.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    dictionary[key] = item.Value.ValueKind == JsonValueKind.String
                        ? item.Value.GetString()?.Trim()
                        : item.Value.ToString()?.Trim();
                }

                return dictionary;
            }

            try
            {
                string json;
                try
                {
                    json = DecryptString(encryptedPayload);
                }
                catch
                {
                    // Backward compatibility for any legacy rows that stored plain JSON.
                    json = encryptedPayload;
                }

                var payload = JsonSerializer.Deserialize<IntegrationSettingsPayload>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                payload ??= new IntegrationSettingsPayload();

                payload.Public = NormalizeSettings(payload.Public);
                payload.Secret = NormalizeSettings(payload.Secret);

                if (payload.Public.Count == 0 && payload.Secret.Count == 0)
                {
                    using var document = JsonDocument.Parse(json);
                    payload.Public = ReadSettingsObject(document.RootElement, "Public", "PublicSettings");
                    payload.Secret = ReadSettingsObject(document.RootElement, "Secret", "SecretSettings");
                }

                return payload;
            }
            catch
            {
                return new IntegrationSettingsPayload();
            }
        }

        private string EncryptSettingsPayload(IntegrationSettingsPayload payload)
        {
            payload ??= new IntegrationSettingsPayload();
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(payload);
            return EncryptString(json);
        }

        private static string NormalizeKey(string value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private bool IsRazorpayProvider(string category, string providerKey, string providerDisplayName, IDictionary<string, string> publicSettings)
        {
            if (!NormalizeKey(category).Equals(IntegrationConstants.Category.Payments, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (NormalizeKey(providerKey).Equals("razorpay", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (NormalizeKey(providerDisplayName).Equals("razorpay", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (publicSettings != null)
            {
                if (publicSettings.TryGetValue("Provider", out var provider)
                    && NormalizeKey(provider).Equals("razorpay", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildRazorpayWebhookUrl(string tenantName, string storeCode, string providerKey, out string webhookUrl)
        {
            webhookUrl = null;

            var baseUrl = _configuration?["IntegrationSettings:MasterApiPublicBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = _configuration?["Services:Master:BaseUrl"];
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return false;
            }

            var webhookRoute = _configuration?["IntegrationSettings:RazorpayWebhookRoute"];
            if (string.IsNullOrWhiteSpace(webhookRoute))
            {
                webhookRoute = _configuration?["Services:Master:RazorpayWebhookRoute"];
            }

            if (string.IsNullOrWhiteSpace(webhookRoute))
            {
                webhookRoute = "/Customer/ReceivePaymentWebhook";
            }

            var trimmedBase = baseUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmedBase, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            webhookRoute = webhookRoute.Trim();
            if (!webhookRoute.StartsWith("/", StringComparison.Ordinal))
            {
                webhookRoute = "/" + webhookRoute;
            }

            var keyMaterial = string.Join("|",
                NormalizeKey(tenantName).ToLowerInvariant(),
                NormalizeKey(storeCode).ToLowerInvariant(),
                NormalizeKey(providerKey).ToLowerInvariant(),
                "razorpay");

            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
            var token = Convert.ToHexString(hashBytes).ToLowerInvariant();
            webhookUrl = $"{trimmedBase}{webhookRoute}?provider=razorpay&ref={token}";
            return true;
        }

        private List<string> ValidateIntegrationConfiguration(
            string category,
            IntegrationSettingsPayload payload,
            string providerKey = null,
            string providerDisplayName = null,
            bool requireRazorpayWebhookSecret = false)
        {
            payload ??= new IntegrationSettingsPayload();
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();

            bool HasPublic(string key)
                => payload.Public.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val);

            bool HasSecret(string key)
                => payload.Secret.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val);

            string Public(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Public.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            bool HasAnySecret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (HasSecret(key))
                    {
                        return true;
                    }
                }

                return false;
            }

            var normalizedCategory = NormalizeKey(category);

            if (normalizedCategory.Equals(IntegrationConstants.Category.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Public("Host", "Endpoint"))) errors.Add("Email host is required.");
                if (string.IsNullOrWhiteSpace(Public("Port", "AccountId"))) errors.Add("Email port is required.");
                if (string.IsNullOrWhiteSpace(Public("From", "SenderId"))) errors.Add("Email sender is required.");
                if (!HasAnySecret("Username", "ApiKey")) errors.Add("Email username/API key is required.");
                if (!HasAnySecret("Password", "Token", "Secret", "SecretKey")) errors.Add("Email password/token/secret is required.");
            }
            else if (normalizedCategory.Equals(IntegrationConstants.Category.Sms, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Public("Provider"))) errors.Add("SMS provider is required.");
                if (string.IsNullOrWhiteSpace(Public("SenderId", "From"))) errors.Add("SMS sender ID is required.");
                if (!HasAnySecret("ApiKey", "AuthToken", "Username", "Password", "Token", "Secret")) errors.Add("SMS API credentials are required.");
            }
            else if (normalizedCategory.Equals(IntegrationConstants.Category.WhatsApp, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(Public("Provider"))) errors.Add("WhatsApp provider is required.");
                if (string.IsNullOrWhiteSpace(Public("PhoneNumberId", "Port", "AccountId"))) errors.Add("WhatsApp phone number/account ID is required.");
                if (!HasAnySecret("AccessToken", "Token", "Password", "Secret")) errors.Add("WhatsApp access token/secret is required.");
            }
            else if (normalizedCategory.Equals(IntegrationConstants.Category.Payments, StringComparison.OrdinalIgnoreCase))
            {
                var isRazorpay = IsRazorpayProvider(category, providerKey, providerDisplayName, payload.Public);
                if (string.IsNullOrWhiteSpace(Public("Provider"))) errors.Add("Payment provider is required.");
                if (!HasAnySecret("ApiKey", "Username", "Token", "KeyId")) errors.Add(isRazorpay ? "Razorpay key ID is required." : "Payment API key/username is required.");
                if (!HasAnySecret("SecretKey", "Password", "Secret", "KeySecret")) errors.Add(isRazorpay ? "Razorpay key secret is required." : "Payment secret key/password is required.");

                if (isRazorpay)
                {
                    if (requireRazorpayWebhookSecret && !HasAnySecret("WebhookSecret"))
                    {
                        errors.Add("Razorpay webhook secret is required.");
                    }

                    var webhookUrl = Public("WebhookUrl");
                    if (string.IsNullOrWhiteSpace(webhookUrl))
                    {
                        errors.Add("Razorpay webhook URL is unavailable. Configure Master API public base URL.");
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(Public("MerchantId", "AccountId", "Port"))) errors.Add("Merchant/account ID is required.");
                    if (string.IsNullOrWhiteSpace(Public("WebhookUrl"))) errors.Add("Webhook URL is required.");
                }
            }

            return errors;
        }

        private async Task<(bool Success, string Message)> ProbeIntegrationConnectionAsync(string category, string providerKey, IntegrationSettingsPayload payload, bool isTestMode)
        {
            payload ??= new IntegrationSettingsPayload();
            payload.Public ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            payload.Secret ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string Public(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Public.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            string Secret(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (payload.Secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }

            var normalizedCategory = NormalizeKey(category);
            if (normalizedCategory.Equals(IntegrationConstants.Category.Email, StringComparison.OrdinalIgnoreCase))
            {
                var host = Public("Host", "Endpoint");
                var portText = Public("Port", "AccountId");
                if (!int.TryParse(portText, out var port) || port <= 0)
                {
                    return (false, "Invalid SMTP port.");
                }

                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(host, port);
                    var completed = await Task.WhenAny(connectTask, Task.Delay(5000));
                    if (completed != connectTask || !client.Connected)
                    {
                        return (false, "SMTP connection timed out.");
                    }

                    return (true, "SMTP host is reachable. Credentials were validated by server-side configuration checks.");
                }
                catch (Exception ex)
                {
                    return (false, $"SMTP connection failed: {ex.Message}");
                }
            }

            if (normalizedCategory.Equals(IntegrationConstants.Category.Payments, StringComparison.OrdinalIgnoreCase))
            {
                var webhookUrl = Public("WebhookUrl");
                if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return (false, "Webhook URL is invalid.");
                }

                if (!isTestMode && uri.Host.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Live mode requires a publicly reachable webhook URL.");
                }

                if (IsRazorpayProvider(category, providerKey, null, payload.Public))
                {
                    var keyId = isTestMode
                        ? Secret("TestKeyId", "Username")
                        : Secret("LiveKeyId", "LiveApiKey", "ApiKey", "Username", "Token", "KeyId");
                    var keySecret = isTestMode
                        ? Secret("TestKeySecret", "Password")
                        : Secret("LiveKeySecret", "LiveSecretKey", "SecretKey", "Password", "Secret", "KeySecret");

                    if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
                    {
                        return (false, isTestMode
                            ? "Razorpay test mode requires TestKeyId and TestKeySecret."
                            : "Razorpay credentials are missing for the selected mode.");
                    }

                    var razorpayApiBaseUrl = _configuration?["IntegrationSettings:RazorpayApiBaseUrl"];
                    if (string.IsNullOrWhiteSpace(razorpayApiBaseUrl))
                    {
                        razorpayApiBaseUrl = "https://api.razorpay.com";
                    }

                    if (!Uri.TryCreate(razorpayApiBaseUrl.Trim(), UriKind.Absolute, out var razorpayBaseUri)
                        || razorpayBaseUri.Scheme != Uri.UriSchemeHttps)
                    {
                        return (false, "Razorpay API base URL is invalid.");
                    }

                    var probeUri = new Uri(razorpayBaseUri, "/v1/orders?count=1");

                    using var httpClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(12)
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
                    var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    try
                    {
                        using var response = await httpClient.SendAsync(request);
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            return (false, isTestMode
                                ? "Razorpay test credentials are invalid. Update Key ID and Key Secret."
                                : "Razorpay live credentials are invalid. Update Key ID and Key Secret.");
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            return (false, $"Razorpay verification failed with status code {(int)response.StatusCode}.");
                        }

                        return (true, isTestMode
                            ? "Razorpay test credentials verified successfully using a read-only API check. No transaction was initiated."
                            : "Razorpay live credentials verified successfully using a read-only API check. No transaction was initiated.");
                    }
                    catch (TaskCanceledException)
                    {
                        return (false, "Razorpay verification timed out. Please try again.");
                    }
                    catch (HttpRequestException)
                    {
                        return (false, "Unable to reach Razorpay API. Please check network connectivity and try again.");
                    }
                    catch
                    {
                        return (false, "Razorpay verification failed due to an unexpected error.");
                    }
                }

                return (true, isTestMode
                    ? "Payment configuration validated in test/sandbox mode. No transaction was initiated."
                    : "Payment configuration validated for live mode. No transaction was initiated.");
            }

            if (normalizedCategory.Equals(IntegrationConstants.Category.WhatsApp, StringComparison.OrdinalIgnoreCase))
            {
                var endpoint = Public("Host", "Endpoint");
                if (!string.IsNullOrWhiteSpace(endpoint)
                    && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
                {
                    return (false, "WhatsApp endpoint/host is invalid.");
                }

                return (true, "WhatsApp configuration validated. No customer message was sent.");
            }

            if (normalizedCategory.Equals(IntegrationConstants.Category.Sms, StringComparison.OrdinalIgnoreCase))
            {
                var endpoint = Public("Host", "Endpoint");
                if (!string.IsNullOrWhiteSpace(endpoint)
                    && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
                {
                    return (false, "SMS endpoint/host is invalid.");
                }

                return (true, "SMS configuration validated. No customer message was sent.");
            }

            return (true, "Integration configuration validated.");
        }

        private byte[] BuildIntegrationEncryptionKey()
        {
            var material = string.IsNullOrWhiteSpace(_integrationEncryptionKeyMaterial)
                ? "LMS_Master_Default_Integration_Key"
                : _integrationEncryptionKeyMaterial;

            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(material));
        }

        private static string GenerateRazorpayWebhookSecret()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToHexString(randomBytes).ToLowerInvariant();
        }

        private static bool IsElsManagedWebhookSecret(Dictionary<string, string> secrets)
        {
            if (secrets == null)
            {
                return false;
            }

            if (!secrets.TryGetValue(InternalWebhookSecretManagedKey, out var managed))
            {
                return false;
            }

            return string.Equals(managed?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWebhookSecretAlreadyRevealed(Dictionary<string, string> secrets)
        {
            if (secrets == null)
            {
                return false;
            }

            if (!secrets.TryGetValue(InternalWebhookSecretRevealedKey, out var revealed))
            {
                return false;
            }

            return string.Equals(revealed?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private string EncryptString(string plainText)
        {
            plainText ??= string.Empty;
            using var aes = Aes.Create();
            aes.Key = BuildIntegrationEncryptionKey();
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(cipherBytes);
        }

        private string DecryptString(string encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText) || !encryptedText.Contains(':'))
            {
                return string.Empty;
            }

            var parts = encryptedText.Split(':');
            if (parts.Length != 2)
            {
                return string.Empty;
            }

            var iv = Convert.FromBase64String(parts[0]);
            var cipherBytes = Convert.FromBase64String(parts[1]);

            using var aes = Aes.Create();
            aes.Key = BuildIntegrationEncryptionKey();
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
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
    CREATE UNIQUE NONCLUSTERED INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo]
    ON [dbo].[LaundryOrderItems] ([TenantName], [StoreCode], [TagNo])
    WHERE [TagNo] IS NOT NULL;
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

        private static TaxConfigurationDto BuildTaxConfigurationDto(
            TaxInvoiceSettingsEntity settings,
            IReadOnlyCollection<TaxInvoiceComponentEntity> components,
            string resolvedTaxMode,
            string fallbackSource)
        {
            var dto = new TaxConfigurationDto
            {
                TenantName = settings.TenantName,
                StoreCode = settings.StoreCode,
                TaxMode = resolvedTaxMode,
                CountryCode = settings.CountryCode,
                CurrencyCode = settings.CurrencyCode,
                IsPricesInclusive = settings.IsPricesInclusive ?? settings.PricesIncludeTax,
                EnableTax = settings.EnableTax,
                GstPercent = settings.GstPercent,
                PricesIncludeTax = settings.PricesIncludeTax,
                InvoicePrefix = settings.InvoicePrefix,
                NextInvoiceNumber = settings.NextInvoiceNumber,
                InvoiceNumberPadding = settings.InvoiceNumberPadding,
                ResetInvoiceNumberYearly = settings.ResetInvoiceNumberYearly,
                Notes = settings.Notes,
                ConcurrencyToken = ToConcurrencyToken(settings.RowVersion),
                LegacyFallbackSource = fallbackSource
            };

            if (components != null)
            {
                foreach (var component in components)
                {
                    dto.Components.Add(MapTaxComponentDto(component));
                }
            }

            return dto;
        }

        private static TaxInvoiceComponentDto MapTaxComponentDto(TaxInvoiceComponentEntity component)
        {
            if (component == null)
            {
                return null;
            }

            return new TaxInvoiceComponentDto
            {
                Id = component.Id,
                Code = component.Code,
                DisplayName = component.DisplayName,
                RatePercent = component.RatePercent,
                Jurisdiction = component.Jurisdiction,
                Priority = component.Priority,
                IsCompound = component.IsCompound,
                IsActive = component.IsActive,
                EffectiveFromUtc = component.EffectiveFromUtc,
                EffectiveToUtc = component.EffectiveToUtc,
                ConcurrencyToken = ToConcurrencyToken(component.RowVersion)
            };
        }

        private static string ValidateTaxConfigurationRequest(TaxConfigurationUpsertRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Length != 2)
            {
                return "CountryCode must be a valid 2-letter ISO code.";
            }

            if (!TryGetCountryTaxProfile(request.CountryCode, out var countryProfile))
            {
                return "CountryCode is not supported. Supported countries: IN, US, GB, AE, DE, FR.";
            }

            if (!SupportedTaxModes.Contains(request.TaxMode))
            {
                return "Tax mode must be one of: NoTax, GST, VAT, SalesTax.";
            }

            if (!string.IsNullOrWhiteSpace(request.CurrencyCode)
                && !string.Equals(request.CurrencyCode, countryProfile.DefaultCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                return $"CurrencyCode for country {request.CountryCode} must be {countryProfile.DefaultCurrencyCode}.";
            }

            if (!string.IsNullOrWhiteSpace(request.CurrencyCode)
                && (request.CurrencyCode.Length < 3 || request.CurrencyCode.Length > 8))
            {
                return "CurrencyCode is invalid.";
            }

            if (request.GstPercent.HasValue && (request.GstPercent.Value < 0 || request.GstPercent.Value > 100))
            {
                return "GST percentage should be between 0 and 100.";
            }

            if (request.NextInvoiceNumber.HasValue && request.NextInvoiceNumber.Value < 1)
            {
                return "Next invoice number should be 1 or greater.";
            }

            if (request.InvoiceNumberPadding.HasValue
                && (request.InvoiceNumberPadding.Value < 1 || request.InvoiceNumberPadding.Value > 12))
            {
                return "Invoice number padding should be between 1 and 12.";
            }

            if (request.Components != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var component in request.Components)
                {
                    if (component == null)
                    {
                        return "Tax component payload is invalid.";
                    }

                    var normalizedCode = NormalizeTaxComponentCode(component.Code);
                    if (string.IsNullOrWhiteSpace(normalizedCode))
                    {
                        return "Tax component code is required.";
                    }

                    var jurisdiction = NormalizeJurisdictionUniquenessKey(component.Jurisdiction);
                    var key = normalizedCode + "|" + component.Priority + "|" + jurisdiction;
                    if (!seen.Add(key))
                    {
                        return "Duplicate tax component code/priority/jurisdiction detected.";
                    }
                }
            }

            return null;
        }

        private static string ValidateTaxComponentRequest(TaxInvoiceComponentUpsertRequestDto request, bool isUpdate)
        {
            if (isUpdate && !request.ComponentId.HasValue)
            {
                return "ComponentId is required for update.";
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return "Tax component code is required.";
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return "Tax component display name is required.";
            }

            if (request.RatePercent < 0 || request.RatePercent > 100)
            {
                return "Tax component rate should be between 0 and 100.";
            }

            if (request.Priority < 1)
            {
                return "Tax component priority should be 1 or greater.";
            }

            if (request.EffectiveFromUtc.HasValue
                && request.EffectiveToUtc.HasValue
                && request.EffectiveFromUtc.Value > request.EffectiveToUtc.Value)
            {
                return "EffectiveFromUtc should be earlier than EffectiveToUtc.";
            }

            return null;
        }

        private static (string ResolvedTaxMode, string ResolutionSource) ResolveTaxModeFromSettings(TaxInvoiceSettingsEntity taxSettings)
        {
            if (taxSettings == null)
            {
                return ("NoTax", "None");
            }

            var normalized = NormalizeTaxMode(taxSettings.TaxMode);
            if (!string.IsNullOrWhiteSpace(normalized) && SupportedTaxModes.Contains(normalized))
            {
                return (normalized, "CountryConfig");
            }

            if (TryGetCountryTaxProfile(taxSettings.CountryCode, out var countryProfile))
            {
                return (countryProfile.DefaultTaxMode, "CountryConfig");
            }

            if (taxSettings.EnableTax && taxSettings.GstPercent > 0)
            {
                return ("GST", "LegacyGSTFallback");
            }

            return ("NoTax", "LegacyGSTFallback");
        }

        private static string NormalizeTaxMode(string taxMode)
        {
            if (string.IsNullOrWhiteSpace(taxMode))
            {
                return null;
            }

            var normalized = taxMode.Trim();
            if (string.Equals(normalized, "notax", StringComparison.OrdinalIgnoreCase))
            {
                return "NoTax";
            }

            if (string.Equals(normalized, "gst", StringComparison.OrdinalIgnoreCase))
            {
                return "GST";
            }

            if (string.Equals(normalized, "vat", StringComparison.OrdinalIgnoreCase))
            {
                return "VAT";
            }

            if (string.Equals(normalized, "salestax", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "sales_tax", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "sales-tax", StringComparison.OrdinalIgnoreCase))
            {
                return "SalesTax";
            }

            return normalized;
        }

        private static string NormalizeCountryCode(string countryCode)
        {
            return string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode.Trim().ToUpperInvariant();
        }

        private static bool TryGetCountryTaxProfile(string countryCode, out CountryTaxProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                return false;
            }

            return SupportedCountryTaxProfiles.TryGetValue(countryCode.Trim().ToUpperInvariant(), out profile);
        }

        private static string NormalizeCurrencyCode(string currencyCode)
        {
            return string.IsNullOrWhiteSpace(currencyCode)
                ? null
                : currencyCode.Trim().ToUpperInvariant();
        }

        private static string NormalizeTaxComponentCode(string code)
        {
            return string.IsNullOrWhiteSpace(code)
                ? null
                : code.Trim().ToUpperInvariant();
        }

        private static string NormalizeJurisdiction(string jurisdiction)
        {
            return string.IsNullOrWhiteSpace(jurisdiction)
                ? null
                : jurisdiction.Trim();
        }

        private static string NormalizeJurisdictionUniquenessKey(string jurisdiction)
        {
            var normalized = NormalizeJurisdiction(jurisdiction);
            return string.IsNullOrWhiteSpace(normalized) ? "__NULL__" : normalized;
        }

        private static bool TryApplyConcurrencyToken(string token, byte[] currentRowVersion, out string message)
        {
            if (currentRowVersion == null || currentRowVersion.Length == 0)
            {
                message = null;
                return true;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                message = "Concurrency token is required for update.";
                return false;
            }

            try
            {
                var incoming = Convert.FromBase64String(token);
                if (incoming.SequenceEqual(currentRowVersion))
                {
                    message = null;
                    return true;
                }

                message = "Data has changed since it was loaded. Refresh and retry.";
                return false;
            }
            catch (FormatException)
            {
                message = "Invalid concurrency token format.";
                return false;
            }
        }

        private static string ToConcurrencyToken(byte[] rowVersion)
        {
            if (rowVersion == null || rowVersion.Length == 0)
            {
                return null;
            }

            return Convert.ToBase64String(rowVersion);
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

        private async Task EnsurePaymentTransactionsTableExistsAsync()
        {
            const string createTableSql = @"
IF OBJECT_ID(N'[dbo].[PaymentTransactions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PaymentTransactions](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [TenantName] [nvarchar](256) NOT NULL,
        [StoreCode] [nvarchar](64) NOT NULL,
        [OrderNo] [nvarchar](64) NOT NULL,
        [InvoiceNo] [nvarchar](32) NULL,
        [CustCode] [nvarchar](64) NOT NULL,
        [GatewayProvider] [nvarchar](32) NOT NULL,
        [ClientRequestId] [nvarchar](64) NOT NULL,
        [GatewayOrderId] [nvarchar](64) NULL,
        [GatewayPaymentId] [nvarchar](64) NULL,
        [GatewaySignature] [nvarchar](512) NULL,
        [Currency] [nvarchar](8) NOT NULL CONSTRAINT [DF_PaymentTransactions_Currency] DEFAULT(N'INR'),
        [AmountRequested] [decimal](18,2) NOT NULL CONSTRAINT [DF_PaymentTransactions_AmountRequested] DEFAULT((0)),
        [AmountAuthorized] [decimal](18,2) NOT NULL CONSTRAINT [DF_PaymentTransactions_AmountAuthorized] DEFAULT((0)),
        [AmountCaptured] [decimal](18,2) NOT NULL CONSTRAINT [DF_PaymentTransactions_AmountCaptured] DEFAULT((0)),
        [Status] [nvarchar](32) NOT NULL,
        [FailureCode] [nvarchar](64) NULL,
        [FailureReason] [nvarchar](512) NULL,
        [IsLiveMode] [bit] NOT NULL CONSTRAINT [DF_PaymentTransactions_IsLiveMode] DEFAULT((0)),
        [CreatedDate] [datetime2](7) NOT NULL,
        [ModifiedDate] [datetime2](7) NOT NULL,
        [RowVersion] [rowversion] NOT NULL,
        CONSTRAINT [PK_PaymentTransactions] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PaymentTransactions_TenantName_StoreCode_OrderNo'
      AND object_id = OBJECT_ID(N'[dbo].[PaymentTransactions]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_PaymentTransactions_TenantName_StoreCode_OrderNo]
    ON [dbo].[PaymentTransactions] ([TenantName], [StoreCode], [OrderNo]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PaymentTransactions_TenantName_StoreCode_ClientRequestId'
      AND object_id = OBJECT_ID(N'[dbo].[PaymentTransactions]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_PaymentTransactions_TenantName_StoreCode_ClientRequestId]
    ON [dbo].[PaymentTransactions] ([TenantName], [StoreCode], [ClientRequestId]);
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PaymentTransactions_GatewayOrderId'
      AND object_id = OBJECT_ID(N'[dbo].[PaymentTransactions]')
      AND (is_unique = 0 OR filter_definition IS NULL OR filter_definition NOT LIKE '%[GatewayOrderId] IS NOT NULL%')
)
BEGIN
    DROP INDEX [IX_PaymentTransactions_GatewayOrderId] ON [dbo].[PaymentTransactions];
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_PaymentTransactions_GatewayOrderId'
      AND object_id = OBJECT_ID(N'[dbo].[PaymentTransactions]'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_PaymentTransactions_GatewayOrderId]
    ON [dbo].[PaymentTransactions] ([GatewayOrderId])
    WHERE [GatewayOrderId] IS NOT NULL;
END;";

            await _masterDbContext.Database.ExecuteSqlRawAsync(createTableSql);
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

