using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMS.Master.BusinessSerive.Interfaces;
using LMS.Master.DTO;
using LMS.Master.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Master.WebApi.Controllers
{
    [Route("Customer")]
    [ApiController]
    public class CustomerController : ControllerBase
    {
        private ICustomer _customer;
          public CustomerController(ICustomer customer)
        {
            this._customer = customer;
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
        /// New Customer
        /// </summary>
        /// <returns></returns>
        [HttpPost(nameof(InsertCustomer))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<CustomerIOResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> InsertCustomer([FromBody] CustomerDto customerDto)
        {
            if (customerDto == null)
                return BadRequest("Invalid data for this operation");
            var insertcustomerResult =await _customer.AddCustomer(customerDto);
           return StatusCode((int)insertcustomerResult.StatusCode, insertcustomerResult.ResultSet);
        }
    }
}