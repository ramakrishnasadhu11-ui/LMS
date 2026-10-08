using LMS.Master.DTO;
using LMS.Master.Utilities;
using System.Threading.Tasks;

namespace LMS.Master.BusinessSerive.Interfaces
{
    public interface ICustomer
    {
        Task<ActionReturnType> AddCustomer(CustomerDto customerDto);
        Task<ActionReturnType> UpdateCustomer(CustomerDto customerDto);
        Task<ActionReturnType> DeleteCustomer(string custCode);
        Task<ActionReturnType> SaveCustomerPreferences(CustomerPreferenceDto preferenceDto);
        Task<ActionReturnType> GetCustomerPreferences(string tenantName, string storeCode);
        Task<ActionReturnType> SavePricingRules(PricingRulesDto pricingRulesDto);
        Task<ActionReturnType> GetPricingRules(string tenantName, string storeCode);
        Task<ActionReturnType> SavePaymentSettings(PaymentSettingsDto paymentSettingsDto);
        Task<ActionReturnType> GetPaymentSettings(string tenantName, string storeCode);
        Task<ActionReturnType> SaveTaxInvoiceSettings(TaxInvoiceSettingsDto taxInvoiceSettingsDto);
        Task<ActionReturnType> GetTaxInvoiceSettings(string tenantName, string storeCode);
        Task<ActionReturnType> SaveWorkflowStatusSettings(WorkflowStatusSettingsDto workflowStatusSettingsDto);
        Task<ActionReturnType> GetWorkflowStatusSettings(string tenantName, string storeCode);
        Task<ActionReturnType> SaveBarcodeTagSettings(BarcodeTagSettingsDto barcodeTagSettingsDto);
        Task<ActionReturnType> GetBarcodeTagSettings(string tenantName, string storeCode);
        Task<ActionReturnType> SaveCustomerAdvance(CustomerAdvanceDto advanceDto);
        Task<ActionReturnType> GetCustomerAdvances(string tenantName, string storeCode, string custCode);
        Task<ActionReturnType> CreateLaundryOrder(LaundryOrderDto orderDto);
        Task<ActionReturnType> GetLaundryOrders(string tenantName, string storeCode, string custCode);
        Task<ActionReturnType> GetStoreLaundryOrders(string tenantName, string storeCode, string status = null, string searchText = null);
        Task<ActionReturnType> UpdateLaundryOrderStatus(string tenantName, string storeCode, string orderNo, string status);
        Task<ActionReturnType> SettleLaundryOrderPayment(string tenantName, string storeCode, string orderNo, decimal paidAmount, string paymentMode = null, string notes = null);
        Task<ActionReturnType> GetLaundryDeliveryStatus(string tenantName, string storeCode);
        Task<ActionReturnType> SaveLaundryItemPrice(LaundryItemPriceDto itemPriceDto);
        Task<ActionReturnType> DeactivateLaundryItemPrice(string tenantName, string storeCode, string serviceType, string category, string itemName);
        Task<ActionReturnType> ReactivateLaundryItemPrice(string tenantName, string storeCode, string serviceType, string category, string itemName);
        Task<ActionReturnType> GetLaundryItemPrices(string tenantName, string storeCode, bool includeInactive = false);
        Task<ActionReturnType> ImportDefaultLaundryItemPrices(string tenantName, string storeCode, bool overwriteExisting);
        Task<ActionReturnType> SaveStoreServiceMaster(StoreServiceMasterDto masterDto);
        Task<ActionReturnType> SetStoreServiceMasterStatus(string tenantName, string storeCode, string masterType, string name, bool isActive);
        Task<ActionReturnType> GetStoreServiceMasters(string tenantName, string storeCode, bool includeInactive = false);
        Task<ActionReturnType> SaveStoreItemMaster(StoreItemMasterDto itemDto);
        Task<ActionReturnType> SetStoreItemMasterStatus(string tenantName, string storeCode, string serviceType, string category, string itemName, bool isActive);
        Task<ActionReturnType> GetStoreItemMasters(string tenantName, string storeCode, bool includeInactive = false);
        ActionReturnType GetCustomerByName(string customerName, string tenantName, string storeCode);
        ActionReturnType GetCustomers(string tenantName, string storeCode);
        ActionReturnType SearchCustomers(string searchText, string tenantName, string storeCode);
    }
}
