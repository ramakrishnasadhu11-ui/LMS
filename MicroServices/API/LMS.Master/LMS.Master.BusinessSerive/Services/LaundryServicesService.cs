using AutoMapper;
using AutoMapper.Configuration;
using LMS.Core.Repository.UnitOfWork;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DataModels.DataContext;
using LMS.Master.DataModels.Entities;
using LMS.Master.DTO.Entities.Dto;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

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
        #region Retive All Assigned LaundryServices
        public async Task<List<AvailableLaundryServicesForClientDto>> GetAllAssignedLaundryServices(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            try
            {
                var ctx = new LMS_Master_DevContext();
                var LaundryServices = await (from e in ctx.AvailableLaundryServicesForClient
                                             where e.ClientId == AvailableLaundryServicesForClientDto.ClientId && e.ClientStoreCode == AvailableLaundryServicesForClientDto.ClientStoreCode
                                             select new AvailableLaundryServicesForClientDto
                                             {
                                                 CustomerServiceTypeId = e.CustomerServiceTypeId,
                                                 CustomerServiceType = e.CustomerServiceType
                                             }).ToListAsync();
                List<AvailableLaundryServicesForClientDto> result = _mapper.Map<List<AvailableLaundryServicesForClientDto>>(LaundryServices);
                return result;
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        #endregion
        #region AssignlaundryserviceByClient
        public int AssignlaundryserviceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            var serviceId = new SqlParameter("@serviceId", SqlDbType.Int);
            serviceId.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Assign_laundry_service_ByClient @ClientId,@ClientStoreCode,@CustomerServiceTypeId,@CustomerServiceType,@CreatedByUserId,@ModifiedByUserId,@serviceId OUT",
                 new SqlParameter("@ClientId", AvailableLaundryServicesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryServicesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerServiceTypeId", AvailableLaundryServicesForClientDto.CustomerServiceTypeId),
                 new SqlParameter("@CustomerServiceType", AvailableLaundryServicesForClientDto.CustomerServiceType),
                 new SqlParameter("@CreatedByUserId", AvailableLaundryServicesForClientDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", AvailableLaundryServicesForClientDto.ModifiedByUserId),
                 @serviceId);
            if (Convert.ToInt32(serviceId.Value) > 0)
                return 1;
            else
                return 0;
        }
        #endregion
        #region UpdateAssignLaundryServiceByClient
        public int UpdateAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            int retValue = 0;
            var status = new SqlParameter("@status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Update_Assign_laundry_service_ByClient @ClientId,@ClientStoreCode,@CustomerServiceTypeId,@CustomerServiceType,@ModifiedByUserId,@status OUT",
                 new SqlParameter("@ClientId", AvailableLaundryServicesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryServicesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerServiceTypeId", AvailableLaundryServicesForClientDto.CustomerServiceTypeId),
                 new SqlParameter("@CustomerServiceType", AvailableLaundryServicesForClientDto.CustomerServiceType),
                 new SqlParameter("@ModifiedByUserId", AvailableLaundryServicesForClientDto.ModifiedByUserId),
                 @status);
            if (Convert.ToInt32(status.Value) == 1)
                retValue = 1;
            else if (Convert.ToInt32(status.Value) == 0)
                retValue = 0;

            return retValue;
        }
        #endregion
        #region DeleteAssignLaundryServiceByClient
        public int DeleteAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            int retValue = 0;
            var status = new SqlParameter("@status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Delete_Assign_laundry_service_ByClient @ClientId,@ClientStoreCode,@CustomerServiceTypeId,@status OUT",
                 new SqlParameter("@ClientId", AvailableLaundryServicesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryServicesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerServiceTypeId", AvailableLaundryServicesForClientDto.CustomerServiceTypeId),
                 @status);
            if (Convert.ToInt32(status.Value) == 1)
                retValue = 1;
            else if (Convert.ToInt32(status.Value) == 0)
                retValue = 0;

            return retValue;
        }
        #endregion

        #region  Assign Laundry CustomerType For Client
        public int AssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            var status = new SqlParameter("@status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Assign_LaundryCustomerType_For_Client @ClientId,@ClientStoreCode,@CustomerTypeId,@CustomerTypeName,@CreatedByUserId,@ModifiedByUserId,@status OUT",
                 new SqlParameter("@ClientId", AvailableLaundryCustomerTypesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryCustomerTypesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerTypeId", AvailableLaundryCustomerTypesForClientDto.CustomerTypeId),
                 new SqlParameter("@CustomerTypeName", AvailableLaundryCustomerTypesForClientDto.CustomerTypeName),
                 new SqlParameter("@CreatedByUserId", AvailableLaundryCustomerTypesForClientDto.CreatedByUserId),
                 new SqlParameter("@ModifiedByUserId", AvailableLaundryCustomerTypesForClientDto.ModifiedByUserId),
                 @status);
            if (Convert.ToInt32(status.Value) > 0)
                return 1;
            else
                return 0;
        }
        #endregion
        #region Update Assign Laundry CustomerType For Client
        public int UpdateAssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            int retValue = 0;
            var status = new SqlParameter("@status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].[usp_Update_Assign_Laundry_CustomerType_For_Client]  @ClientId,@ClientStoreCode,@CustomerTypeId,@CustomerTypeName,@ModifiedByUserId,@status OUT",
                 new SqlParameter("@ClientId", AvailableLaundryCustomerTypesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryCustomerTypesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerTypeId", AvailableLaundryCustomerTypesForClientDto.CustomerTypeId),
                 new SqlParameter("@CustomerTypeName", AvailableLaundryCustomerTypesForClientDto.CustomerTypeName),
                 new SqlParameter("@ModifiedByUserId", AvailableLaundryCustomerTypesForClientDto.ModifiedByUserId),
                 @status);
            if (Convert.ToInt32(status.Value) == 1)
                retValue = 1;
            else if (Convert.ToInt32(status.Value) == 0)
                retValue = 0;

            return retValue;
        }

        #endregion
        #region Delete Assign Laundry CustomerType By Client
        public int DeleteAssignLaundryCustomerTypeByClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            var ctx = new LMS_Master_DevContext();
            int retValue = 0;
            var status = new SqlParameter("@status", SqlDbType.Int);
            status.Direction = ParameterDirection.Output;
            ctx.Database.ExecuteSqlCommand("[dbo].usp_Delete_Assign_Laundry_CustomerType_ByClient @ClientId,@ClientStoreCode,@CustomerTypeId,@status OUT",
                 new SqlParameter("@ClientId", AvailableLaundryCustomerTypesForClientDto.ClientId),
                 new SqlParameter("@ClientStoreCode", AvailableLaundryCustomerTypesForClientDto.ClientStoreCode),
                 new SqlParameter("@CustomerTypeId", AvailableLaundryCustomerTypesForClientDto.CustomerTypeId),
                 @status);
            if (Convert.ToInt32(status.Value) == 1)
                retValue = 1;
            else if (Convert.ToInt32(status.Value) == 0)
                retValue = 0;

            return retValue;
        }
        #endregion

        #region Get All Assigned LaundryCustomer Type For Client
        public async Task<List<AvailableLaundryCustomerTypesForClientDto>> GetAllAssignedLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
                var ctx = new LMS_Master_DevContext();
                var LaundryServiceCustomerTypes = await (from ct in ctx.AvailableLaundryCustomerTypesForClient
                                                         where ct.ClientId == AvailableLaundryCustomerTypesForClientDto.ClientId && ct.ClientStoreCode == AvailableLaundryCustomerTypesForClientDto.ClientStoreCode
                                                         select new AvailableLaundryCustomerTypesForClientDto
                                                         {
                                                             CustomerTypeId = ct.CustomerTypeId,
                                                             CustomerTypeName = ct.CustomerTypeName
                                                         }).ToListAsync();
                List<AvailableLaundryCustomerTypesForClientDto> result = _mapper.Map<List<AvailableLaundryCustomerTypesForClientDto>>(LaundryServiceCustomerTypes);
                return result;
        }
        #endregion
    }
}
