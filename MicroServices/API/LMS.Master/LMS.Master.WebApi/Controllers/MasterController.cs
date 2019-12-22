using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO.Entities.Dto;
using LMS.Master.WebApi.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;

namespace LMS.Master.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MasterController : ControllerBase
    {
        private readonly ILaundryServicesService _laundryServicesService;
        public MasterController(ILaundryServicesService laundryServicesService)
        {
            this._laundryServicesService = laundryServicesService;
        }

        /// <summary>
        /// Test Service
        /// </summary>
        /// <returns></returns>
        [HttpGet(nameof(TestService))]
        public string TestService()
        {
            return "I am Live";
        }


        /// <summary>
        /// Get All Assigned Laundry Services to the client
        /// </summary>
        /// <returns></returns>

        [HttpGet(nameof(GetAllAssignedLaundryServices))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<APIResponse>> GetAllAssignedLaundryServices([FromQuery]  AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            List<AvailableLaundryServicesForClientDto> result = await _laundryServicesService.GetAllAssignedLaundryServices(AvailableLaundryServicesForClientDto);
            if (result.Count > 0 && result != null)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), result);
            else if (result.Count == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), result);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), 500);
        }

        /// <summary>
        /// Assign Laundryservice to the Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(AssignlaundryserviceByClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> AssignlaundryserviceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {

            if (AvailableLaundryServicesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int laundryServiceId = _laundryServicesService.AssignlaundryserviceByClient(AvailableLaundryServicesForClientDto);
            if (laundryServiceId > 0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), laundryServiceId);
            else if (laundryServiceId == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), laundryServiceId);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), laundryServiceId);
        }

        /// <summary>
        /// Update Assigned Laundry Service by Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(UpdateAssignLaundryServiceByClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> UpdateAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {

            if (AvailableLaundryServicesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int status = _laundryServicesService.UpdateAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto);
            if (status == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), status);
            else if (status == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), status);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), status);
        }

        // <summary>
        /// Delete Assign LaundryService By Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(DeleteAssignLaundryServiceByClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> DeleteAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto AvailableLaundryServicesForClientDto)
        {
            if (AvailableLaundryServicesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int status = _laundryServicesService.DeleteAssignLaundryServiceByClient(AvailableLaundryServicesForClientDto);
            if (status == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), status);
            else if (status == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), status);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), status);
        }

        /// <summary>
        /// Get All Assigned Laundry CustomerType For Client
        /// </summary>
        /// <returns></returns>

        [HttpGet(nameof(GetAllAssignedLaundryCustomerTypeForClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<APIResponse>> GetAllAssignedLaundryCustomerTypeForClient([FromQuery]  AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            List<AvailableLaundryCustomerTypesForClientDto> result = await _laundryServicesService.GetAllAssignedLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto);
            if (result.Count > 0 && result != null)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), result);
            else if (result.Count == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), result);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), 500);
        }


        /// <summary>
        /// Assign LaundryCustomer Type For Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(AssignLaundryCustomerTypeForClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> AssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            if (AvailableLaundryCustomerTypesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int status = _laundryServicesService.AssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto);
            if (status == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), status);
            else if (status == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), status);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), status);
        }
        /// <summary>
        /// Update Assign Laundry CustomerType For Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(UpdateAssignLaundryCustomerTypeForClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> UpdateAssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            if (AvailableLaundryCustomerTypesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int status = _laundryServicesService.UpdateAssignLaundryCustomerTypeForClient(AvailableLaundryCustomerTypesForClientDto);
            if (status == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), status);
            else if (status == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), status);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), status);
        }

        /// <summary>
        /// Delete Assign LaundryCustomer Type By Client
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(DeleteAssignLaundryCustomerTypeByClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> DeleteAssignLaundryCustomerTypeByClient(AvailableLaundryCustomerTypesForClientDto AvailableLaundryCustomerTypesForClientDto)
        {
            if (AvailableLaundryCustomerTypesForClientDto == null)
                return BadRequest("Invalid data for this operation");
            int status = _laundryServicesService.DeleteAssignLaundryCustomerTypeByClient(AvailableLaundryCustomerTypesForClientDto);
            if (status == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), status);
            else if (status == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), status);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), status);
        }

    }
}