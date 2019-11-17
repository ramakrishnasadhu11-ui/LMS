using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.BusinessSerive.Services;
using LMS.Identity.DTO.Entities.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;

namespace LMS.Identity.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private ILoginService _login;

        public LoginController(ILoginService login)
        {
            this._login = login;
        }

         [HttpPost(nameof(Login))]
        public async Task<int> Login([FromBody] ClientDto ClientDto)
        {
            int clientId = await _login.AddClient(ClientDto);
            //    if (clientId>0)
            //        return new APIResponse(StatusCodes.Status200OK, GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), true);
            //    else
            //        throw new ApiException(GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), StatusCodes.Status500InternalServerError, ModelState.AllErrors());

            return clientId;
        }
    }
}