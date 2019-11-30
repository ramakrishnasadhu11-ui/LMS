using System;
using System.Collections.Generic;
using System.Data;
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
            var clientId = new SqlParameter("@ClientId", SqlDbType.Int);
            clientId.Direction = ParameterDirection.Output;
           
                 int status =ctx.Database.ExecuteSqlCommand("usp_insert_client @ClientGenderID,@ClientNam,@phoneNumber,@EMail,@MiddleName,@FamilyName,@Photo,@Address1,@Address2,@City,@Region,@Zip,@Country,@Active,@CreatedByUserId,@ModifiedByUserId,@ClientId OUT",
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
                 new SqlParameter("@Country", ClientDto.Country),
                 new SqlParameter("@Active", ClientDto.Active),
                 new SqlParameter("@CreatedByUserId", ClientDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", ClientDto.ModifiedByUserId),
                 clientId);

            if (status == 1)
                return Convert.ToInt32(clientId.Value);
            else
                return 0;
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
