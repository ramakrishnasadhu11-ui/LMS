using LMS.Identity.DTO.Entities.Dto;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
        int RegisterUser(ClientDto ClientDto);
        Task<List<UserGenderDto>> UserGender();
        Task<int> ChangePassword(string email, string NewPassword, string OldPassword);
        Task<int> CheckUserEmailExist(string email);
    }
}
