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
        private ILaundryServicesService _laundryServicesService;
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
        /// Add Laundry Service
        /// </summary>
        /// <returns></returns>

        [HttpPost(nameof(AddLaundryService))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> AddLaundryService(LaundryServicesDto LaundryServicesDto)
        {

            if (LaundryServicesDto == null)
                return BadRequest("Invalid data for this operation");
            int laundryServiceId = _laundryServicesService.AddLaundryServices(LaundryServicesDto);
            if (laundryServiceId > 0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), laundryServiceId);
            else if (laundryServiceId == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), laundryServiceId);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), laundryServiceId);
        }
    }
}