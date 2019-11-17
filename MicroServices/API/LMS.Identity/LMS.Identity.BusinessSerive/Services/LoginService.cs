using System;
using System.Threading.Tasks;
using AutoMapper;
using LMS.Core.Repository.UnitOfWork;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.DataModels.Entities;
using LMS.Identity.DTO.Entities.Dto;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
        private IUnitOfWork _unitOfWork;
        private IMapper _mapper;

        public LoginService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        #region AddClient
        public async Task<int> AddClient(ClientDto ClientDto)
        {
             
            Client Client = new Client();
            Client.ClientGenderId = ClientDto.ClientGenderId;
            Client.ClientName = ClientDto.ClientName;
            Client.PhoneNumber = ClientDto.PhoneNumber;
            Client.Email = ClientDto.Email;
            Client.MiddleName = ClientDto.MiddleName;
            Client.FamilyName = ClientDto.FamilyName;
            Client.Photo = ClientDto.Photo;
            Client.Address1 = ClientDto.Address1;
            Client.Address2 = ClientDto.Address2;
            Client.City = ClientDto.City;
            Client.Region = ClientDto.Region;
            Client.Zip = ClientDto.Zip;
            Client.Active = ClientDto.Active;
            Client.CreatedByUserId = ClientDto.CreatedByUserId;
            Client.CreatedDate = ClientDto.CreatedDate;
            Client.ModifiedByUserId = ClientDto.ModifiedByUserId;
            Client.ModifiedDate = ClientDto.ModifiedDate;
            await _unitOfWork.GetRepository<Client>().InsertAsync(Client);
            int clientId = await _unitOfWork.SaveChangesAsync();
            return clientId;
        }
        #endregion
    }
}
