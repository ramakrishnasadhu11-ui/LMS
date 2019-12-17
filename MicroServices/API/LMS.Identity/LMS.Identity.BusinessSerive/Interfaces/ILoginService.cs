using LMS.Identity.DTO;
using LMS.Identity.DTO.Entities.Dto;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
        int RegisterUser(CustomerDto ClientDto);
        Task<List<UserGenderDto>> UserGender();
        Task<int> ClientChangePassword(string email, string NewPassword, string OldPassword);
        Task<int> CheckUserEmailExist(string email);
        Task<int> ForgotPassword(string email);
        Task<int> GetEmailCount(string email);
        int ClientLogin(ClientLoginDto ClientLoginDto);
        int RegisterClient(ClientDto ClientDto);
    }
}
