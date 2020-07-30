using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities;
using LMS.Identity.Utilities;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
       Task<ActionReturnType> Register(TenantDto tenantDto);
       Task<ActionReturnType> TenantLogin(string eMail,string password);
       Task<ActionReturnType> changePassword (string eMail,string oldPassword,string newPassword);
       Task<ActionReturnType> forgotPassword(string eMail);
       Task<ActionReturnType> CheckTenantEmail(string eMail);
       Task<ActionReturnType> GetTenantStoreDetails(string eMail);
        Task<ActionReturnType> CheckIsPasswordChangedBytenant(string eMail);
         Task<ActionReturnType> changepassword(string Email,string NewPassword,string OldPassword);

        
        

        
        
    }
}
