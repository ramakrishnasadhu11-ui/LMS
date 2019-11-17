using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.DTO.Entities.Dto;
using Microsoft.AspNetCore.Http;
using LMS.Identity.WebApi.Utility;
using LMS.Identity.WebApi.Api.Utility;

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

         [HttpPost]
        public async Task<APIResponse> Login(ClientDto ClientDto)
        {
            var clientId = await _login.AddClient(ClientDto);
            if (clientId>0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), clientId);
            else if (clientId == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), clientId);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), clientId);

        }
      
    }
}