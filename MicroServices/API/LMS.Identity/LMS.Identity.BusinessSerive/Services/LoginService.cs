using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AutoMapper;
using LMS.Core.Repository.UnitOfWork;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.DataModels.DataContext;
using LMS.Identity.DataModels.Entities;
using LMS.Identity.DTO.Entities.Dto;
using Microsoft.EntityFrameworkCore;

namespace LMS.Identity.BusinessSerive.Services
{
    public class LoginService : ILoginService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public LoginService(IUnitOfWork unitOfWork, IMapper mapper)
        {
           this._unitOfWork = unitOfWork;
            this._mapper = mapper;
        }

        #region AddClient
        public async Task<int> RegisterUser(ClientDto ClientDto)
        {
            var ctx = new LMSDB_DevContext();
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

            object[] xparams = {
                new SqlParameter("@ClientGenderID", ClientDto.ClientGenderId),
                 new SqlParameter("@ClientNam", ClientDto.ClientName),
                 new SqlParameter("@phoneNumber", ClientDto.PhoneNumber),
                 new SqlParameter("@EMail", ClientDto.Email),
                 new SqlParameter("@MiddleName", ClientDto.MiddleName),
                 new SqlParameter("@FamilyName", ClientDto.FamilyName),
                 new SqlParameter("@Photo", ClientDto.Photo),
                 new SqlParameter("@Address1", ClientDto.Address1),
                 new SqlParameter("@Address2", ClientDto.Address2),
                 new SqlParameter("@City", ClientDto.City),
                 new SqlParameter("@Region", ClientDto.Region),
                 new SqlParameter("@Zip", ClientDto.Zip),
                 new SqlParameter("@Country", ClientDto.Active),
                 new SqlParameter("@Active", ClientDto.CreatedByUserId),
                 new SqlParameter("@CreatedByUserId", ClientDto.ModifiedByUserId)
            };


            ctx.Database.ExecuteSqlCommand("exec usp_insert_client @ClientGenderID,@ClientNam,@phoneNumber,@EMail,@MiddleName,@FamilyName,@Photo,@Address1,@Address2,@City,@Region,@Zip,@Country,@Active,@CreatedByUserId,@ModifiedByUserId", xparams);

            //  await _unitOfWork.GetRepository<Client>().InsertAsync(Client);
            // int clientId = await _unitOfWork.SaveChangesAsync();
            // return clientId;
            return 1;
        }
        #endregion
        public async Task<List<UserGenderDto>> UserGender()
        {
            var ctx = new LMSDB_DevContext();
                var list = await ctx.UserGender.FromSql("exec[dbo].[usp_get_gender]").ToListAsync();
            return _mapper.Map<List<UserGenderDto>>(list);
        }


    }
}
