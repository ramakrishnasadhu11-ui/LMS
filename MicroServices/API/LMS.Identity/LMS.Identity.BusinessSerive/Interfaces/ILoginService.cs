using LMS.Identity.DTO.Entities.Dto;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LMS.Identity.BusinessSerive.Interfaces
{
    public interface ILoginService
    {
        Task<int> RegisterUser(ClientDto ClientDto);
        Task<List<UserGenderDto>> UserGender();
     }
}
