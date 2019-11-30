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
        /// Register User
        /// </summary>
        /// <returns></returns>
        [HttpPost(nameof(RegisterUser))]
        public async Task<APIResponse> RegisterUser(ClientDto ClientDto)
        {
            var clientId = await _login.RegisterUser(ClientDto);
            if (clientId > 0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), clientId);
            else if (clientId == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), clientId);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), clientId);

        }

        /// <summary>
        /// Get User Gender
        /// </summary>
        [HttpGet(nameof(GetUserGenders))]
        public async Task<APIResponse> GetUserGenders()
        {
            var genderList = await _login.UserGender();
            if (genderList.Count>0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), genderList);
            else if (genderList.Count == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), genderList);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError),500);
        }
    }
}