using LMS.Identity.DTO;
using LMS.Identity.Utilities;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
        Task<ActionReturnType> Register(TenantDto tenantDto);
        Task<ActionReturnType> TenantLogin(string eMail, string password, string storeCode = null);
        Task<ActionReturnType> TenantProfileDetails(string eMail);
        Task<ActionReturnType> ChangePassword(string eMail, string oldPassword, string newPassword);
        Task<ActionReturnType> ForgotPassword(string eMail);
        Task<ActionReturnType> CheckTenantEmail(string eMail);
        Task<ActionReturnType> GetTenantStoreDetails(string eMail);
        Task<ActionReturnType> CheckIsPasswordChangedByTenant(string eMail);
        Task<ActionReturnType> ChangePasswordForTenant(string email, string newPassword, string oldPassword);
        Task<ActionReturnType> ForgotPasswordForTenant(string email);
        Task<ActionReturnType> CreateStoreUser(StoreUserDto model);
        Task<ActionReturnType> CreateTenantStore(string tenantEmail, string storeCode);
        Task<ActionReturnType> GetStoreUsers(string tenantEmail, string storeCode);
        Task<ActionReturnType> SetStoreUserActiveStatus(string tenantEmail, string storeCode, string userEmail, bool isActive);
        Task<ActionReturnType> GetStoreConfiguration(string tenantEmail, string storeCode);
        Task<ActionReturnType> NewStoreConfiguration(NewStoreConfigurationDto model);
        Task<ActionReturnType> GetTenantStoreStatuses(string tenantEmail);
        Task<ActionReturnType> GetAllStoreStatuses();
        Task<ActionReturnType> SetTenantStoreActiveStatus(string tenantEmail, string storeCode, bool isActive, string activatedBy);
        Task<ActionReturnType> SetStoreActiveStatusByStoreCode(string storeCode, bool isActive, string activatedBy);

        Task<ActionReturnType> GetAllTenantApprovals();

        Task<ActionReturnType> SetTenantApprovalStatus(string tenantId, string approvalStatus, string reason, string actionedBy);
    }
}
