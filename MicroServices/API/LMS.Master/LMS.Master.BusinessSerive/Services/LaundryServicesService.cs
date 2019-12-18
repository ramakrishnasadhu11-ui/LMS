using AutoMapper;
using AutoMapper.Configuration;
using LMS.Core.Repository.UnitOfWork;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DataModels.DataContext;
using LMS.Master.DTO.Entities.Dto;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace LMS.Master.BusinessSerive.Services
{
   public class LaundryServicesService : ILaundryServicesService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;
        public LaundryServicesService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            this._unitOfWork = unitOfWork;
            this._mapper = mapper;
        }
        #region AddLaundryService
        public int AddLaundryServices(LaundryServicesDto LaundryServicesDto)
        {
            var ctx = new LMS_Master_DevContext();
            var serviceId = new SqlParameter("@serviceId", SqlDbType.Int);
            serviceId.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Insert_laundry_services @ClientId,@ClientStoreCode,@ServiceCode,@ServiceName,@CreatedByUserId,@ModifiedByUserId,@serviceId OUT",
                 new SqlParameter("@ClientId", LaundryServicesDto.ClientId),
                 new SqlParameter("@ClientStoreCode", LaundryServicesDto.ClientStoreCode),
                 new SqlParameter("@ServiceCode", LaundryServicesDto.ServiceCode),
                 new SqlParameter("@ServiceName", LaundryServicesDto.ServiceName),
                 new SqlParameter("@CreatedByUserId", LaundryServicesDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", LaundryServicesDto.ModifiedByUserId),
                 @serviceId);
            if (Convert.ToInt32(serviceId.Value) > 0)
                return 1;
            else
                return 0;
        }
        #endregion
    }
}
