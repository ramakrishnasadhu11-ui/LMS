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
        public ActionResult<APIResponse> RegisterUser(CustomerDto CustomerDto)
        {
            if(CustomerDto == null)
                return BadRequest("Invalid data for this operation");
            int clientId = _login.RegisterUser(CustomerDto);
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


        #region
        /// <summary>
        /// Check Client Email Exist or not
        /// </summary>
        [HttpGet(nameof(CheckClientEmail))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<APIResponse> CheckClientEmail(string eMail)
        {
            int statusvalue = await _login.CheckUserEmailExist(eMail);
            if (statusvalue == 1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), statusvalue);
            else if (statusvalue == 0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), statusvalue);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), 500);
        }
        #endregion
        #region Password
        /// <summary>
        /// User can change password
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
                int status = await _login.ClientChangePassword(userEmail, NewPassword, OldPassword);
                if (status == 1)
                {
                    LoginStatusDTO.Message = "Your password has been changed successfully";
                    LoginStatusDTO.MessageStatus = "Success";
                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                }
                else if (status == 0)
                {
                    LoginStatusDTO.Message = "Your current password does not match.";
                    LoginStatusDTO.MessageStatus = "Fail";
                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                }
            }
            else
            {
                LoginStatusDTO.Message = "The Email supplied was not found.";
                LoginStatusDTO.MessageStatus = "Fail";
            }
            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);


        }

        /// <summary>
        /// User can forget password
        /// </summary>
        [HttpGet(nameof(ForgotLoginPassword))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<APIResponse> ForgotLoginPassword(string userEmail)
        {
            LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
            int statusvalue = 0;
            if (!string.IsNullOrEmpty(userEmail))
            {
                statusvalue = await _login.CheckUserEmailExist(userEmail);
                if (statusvalue == 1)
                {
                    //Checking for email count
                    int emailCount = 0;
                    emailCount= _login.GetEmailCount(userEmail);
                    if(emailCount==0)
                    {
                        LoginStatusDTO.Message = "No Email address found for this user name, please contact your Administrator to reset your password";
                        LoginStatusDTO.MessageStatus = "Fail";
                        return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                    }
                    else
                    {
                        statusvalue = await _login.ForgotPassword(userEmail);
                        if(statusvalue==1)
                        {
                            LoginStatusDTO.Message = "Your password has been reset and your new password has been send to your email";
                            LoginStatusDTO.MessageStatus = "Success";
                            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                        }
                        else if (statusvalue == 0)
                        {
                            LoginStatusDTO.Message = "No Email address found for this user name, please contact your personal rep to reset your password";
                            LoginStatusDTO.MessageStatus = "Success";
                            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
                        }
                    }
                }
                else
                {
                        LoginStatusDTO.Message = "The Email supplied was not found";
                        LoginStatusDTO.MessageStatus = "Fail";
                }
            }
            else
            {
                LoginStatusDTO.Message = "Email is Required";
                LoginStatusDTO.MessageStatus = "Fail";
            }
            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        }
        #endregion


        [HttpPost(nameof(RegisterClient))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> RegisterClient(ClientDto ClientDto)
        {
            if (ClientDto == null)
                return BadRequest("Invalid data for this operation");
            int clientId = _login.RegisterClient(ClientDto);
            if (clientId > 0)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), clientId);
            else if (clientId == 0)
                return new APIResponse(StatusCodes.Status204NoContent, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status204NoContent), clientId);
            else if(clientId == -1)
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), clientId);
            else
                throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), 0);

        }

        [HttpGet(nameof(ClientLogin))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> ClientLogin([FromQuery] ClientLoginDto ClientLoginDto)
        {
            int returvalue;
            LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
            if (ClientLoginDto == null)
                return BadRequest("Invalid data for this operation");
            returvalue = _login.ClientLogin(ClientLoginDto);
            if (returvalue == 1)
            {
                LoginStatusDTO.Message = "Login Sucessfull";
                LoginStatusDTO.MessageStatus = "Success";
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
            }
            else if(returvalue == 3)
            {
                LoginStatusDTO.Message = "The Email supplied was not found";
                LoginStatusDTO.MessageStatus = "Fail";
                return new APIResponse(StatusCodes.Status404NotFound, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
            }
            else if (returvalue == 0)
            {
                LoginStatusDTO.Message = "The Password supplied was not found";
                LoginStatusDTO.MessageStatus = "Fail";
                return new APIResponse(StatusCodes.Status404NotFound, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
            }
            else if (returvalue == 2)
            {
                LoginStatusDTO.Message = "The Store Code supplied was not found";
                LoginStatusDTO.MessageStatus = "Fail";
                return new APIResponse(StatusCodes.Status404NotFound, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
            }
            return new APIResponse(StatusCodes.Status500InternalServerError, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        }

        [HttpGet(nameof(GetClientStoreDetails))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<APIResponse> GetClientStoreDetails([FromQuery] string eMail)
        {
            string clientStoreDetails= _login.GetClientStoreDetails(eMail);
            LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
            if (clientStoreDetails.Length >=1)
            {
                LoginStatusDTO.Message = "Success";
                LoginStatusDTO.MessageStatus = "Success";
                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), clientStoreDetails);
            }
            else if (string.IsNullOrEmpty(clientStoreDetails))
            {
                    LoginStatusDTO.Message = "Fail";
                    LoginStatusDTO.MessageStatus = "Fail";
                    return new APIResponse(StatusCodes.Status404NotFound, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
           }
            else
            throw new ApiException(Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status500InternalServerError), 0);
        }


    }
}