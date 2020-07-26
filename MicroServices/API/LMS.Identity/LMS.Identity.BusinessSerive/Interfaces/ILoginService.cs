using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities;
using LMS.Identity.Utilities;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
       Task<ActionReturnType> Register(TenantDto tenantDto);
        //int RegisterUser(CustomerDto ClientDto);
        //Task<List<UserGenderDto>> UserGender();
        //Task<int> ClientChangePassword(string email, string NewPassword, string OldPassword);
        //Task<int> CheckUserEmailExist(string email);
        //Task<int> ForgotPassword(string email);
        //int GetEmailCount(string email);
        //int ClientLogin(ClientLoginDto ClientLoginDto);
        //int RegisterClient(ClientDto ClientDto);

        //string GetClientStoreDetails(string eMail);

        //bool CheckIsPasswordChangedByclient(string eMail);
    }
}
