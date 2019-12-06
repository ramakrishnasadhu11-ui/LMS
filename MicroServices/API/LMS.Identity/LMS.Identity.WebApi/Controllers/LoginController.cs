using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using VMD.RESTApiResponseWrapper.Core.Wrappers;
using LMS.Identity.BusinessSerive.Interfaces;
using LMS.Identity.DTO.Entities.Dto;
using Microsoft.AspNetCore.Http;
using LMS.Identity.WebApi.Utility;
using LMS.Identity.WebApi.Api.Utility;
using LMS.Identity.DTO;

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
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> RegisterUser(ClientDto ClientDto)
        {
            if(ClientDto==null)
                return BadRequest("Invalid data for this operation");
            int clientId = _login.RegisterUser(ClientDto);
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
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

        /// <summary>
        /// Check User Email Exist or Not
        /// </summary>
        [HttpGet(nameof(ChangePassword))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<APIResponse> ChangePassword(string userEmail, string NewPassword, string OldPassword)
        {

            LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
            int statusvalue = await _login.CheckUserEmailExist(userEmail);

            if (statusvalue == 1)
            {
                int status = await _login.ChangePassword(userEmail, NewPassword, OldPassword);
                if (status == 1)
                {
                    LoginStatusDTO.Message = "Your password has been changed successfully";
                    LoginStatusDTO.MessageStatus = "Success";
                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                }
                else if (status == 0)
                {
                    LoginStatusDTO.Message = "Your current password does not match";
                    LoginStatusDTO.MessageStatus = "Fail";
                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                }
            }
            else
            {
                LoginStatusDTO.Message = "Invalid Email";
                LoginStatusDTO.MessageStatus = "Fail";
            }
            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);


        }
    }
}