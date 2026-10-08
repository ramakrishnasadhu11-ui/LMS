using System.Threading.Tasks;
using LMSWebUI.Models.Admin;

namespace LMSWebUI.Services
{
    public interface IStoreConfigurationService
    {
        Task<StoreConfigurationViewModel> LoadAsync(string tenantName, string storeCode);
        Task<(bool Success, string Message)> SaveAsync(StoreConfigurationViewModel model);
        StoreConfigurationViewModel Normalize(StoreConfigurationViewModel model);
        bool HasData(StoreConfigurationViewModel model);
    }
}
