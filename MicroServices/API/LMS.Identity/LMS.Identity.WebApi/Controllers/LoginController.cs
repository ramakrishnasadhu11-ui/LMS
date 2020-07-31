using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using LMS.Identity.BusinessSerive.Interfaces;
using Microsoft.AspNetCore.Http;
using LMS.Identity.DTO;
using LMS.Identity.Utilities;

namespace LMS.Identity.WebApi.Controllers
{
    [Route("Login")]
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
        /// Register Tenant
        /// </summary>
        /// <returns></returns>
        [HttpPost(nameof(RegisterTenant))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> RegisterTenant([FromBody] TenantDto tenantDto)
        {
            if (tenantDto == null)
                return BadRequest("Invalid data for this operation");
            var insertTenantResult =await _login.Register(tenantDto);
           return StatusCode((int)insertTenantResult.StatusCode, insertTenantResult.ResultSet);
        }


        #region
        /// <summary>
        /// Check Tenant Email Exist or not
        /// </summary>
        [HttpGet(nameof(TenantLogin))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> TenantLogin(string eMail,string password)
        {
         var loginResult = await _login.TenantLogin(eMail,password);
           return StatusCode((int)loginResult.StatusCode, loginResult.ResultSet); 
        }

        /// <summary>
        /// ChangePassword 
        /// </summary>
        [HttpGet(nameof(changePassword))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> changePassword(string eMail,string oldPassword,string newPassword)
        {
         var changepasswordResult = await _login.changePassword(eMail,oldPassword,newPassword);
           return StatusCode((int)changepasswordResult.StatusCode, changepasswordResult.ResultSet); 
        }
       
         /// <summary>
        /// forgotPassword 
        /// </summary>
        [HttpGet(nameof(forgotPassword))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> forgotPassword(string eMail)
        {
         var forgotpasswordResult = await _login.forgotPassword(eMail);
           return StatusCode((int)forgotpasswordResult.StatusCode, forgotpasswordResult.ResultSet); 
        }

         /// <summary>
        /// CheckTenantEmail 
        /// </summary>
        [HttpGet(nameof(CheckTenantEmail))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> CheckTenantEmail(string eMail)
        {
         var forgotpasswordResult = await _login.CheckTenantEmail(eMail);
           return StatusCode((int)forgotpasswordResult.StatusCode, forgotpasswordResult.ResultSet); 
        }


         /// <summary>
        /// GetTenantStoreDetails 
        /// </summary>
        [HttpGet(nameof(GetTenantStoreDetails))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> GetTenantStoreDetails(string eMail)
        {
         var forgotpasswordResult = await _login.GetTenantStoreDetails(eMail);
           return StatusCode((int)forgotpasswordResult.StatusCode, forgotpasswordResult.ResultSet); 
        }

         /// <summary>
        /// CheckIsPasswordChangedBytenant 
        /// </summary>
        [HttpGet(nameof(CheckIsPasswordChangedBytenant))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
       public async Task<ActionResult> CheckIsPasswordChangedBytenant(string eMail)
        {
            var passwordChangedBytenantStatus = await _login.CheckIsPasswordChangedBytenant(eMail);
           return StatusCode((int)passwordChangedBytenantStatus.StatusCode, passwordChangedBytenantStatus.ResultSet); 
        }

         /// <summary>
        /// change password 
        /// </summary>
        [HttpPost(nameof(changepassword))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
       public async Task<ActionResult> changepassword([FromBody] ChangePasswordDto ChangePasswordDto)
        {
            var passwordChangedBytenantStatus = await _login.changepassword(ChangePasswordDto.Email,ChangePasswordDto.NewPassword,ChangePasswordDto.OldPassword);
           return StatusCode((int)passwordChangedBytenantStatus.StatusCode, passwordChangedBytenantStatus.ResultSet); 
        }

           /// <summary>
        /// change password 
        /// </summary>
        [HttpPost(nameof(forgotpassword))]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
       public async Task<ActionResult> forgotpassword([FromBody] ChangePasswordDto ChangePasswordDto)
        {
            var forgotpasswordStatus = await _login.forgotpassword(ChangePasswordDto.Email);
           return StatusCode((int)forgotpasswordStatus.StatusCode, forgotpasswordStatus.ResultSet); 
        }


        #endregion
        //#region Password
        ///// <summary>
        ///// User can change password
        ///// </summary>
        //[HttpGet(nameof(ChangePassword))]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status400BadRequest)]
        //public async Task<APIResponse> ChangePassword(string userEmail, string NewPassword, string OldPassword)
        //{

        //    LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
        //    int statusvalue = await _login.CheckUserEmailExist(userEmail);

        //    if (statusvalue == 1)
        //    {
        //        int status = await _login.ClientChangePassword(userEmail, NewPassword, OldPassword);
        //        if (status == 1)
        //        {
        //            LoginStatusDTO.Message = "Your password has been changed successfully";
        //            LoginStatusDTO.MessageStatus = "Success";
        //            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //        }
        //        else if (status == 0)
        //        {
        //            LoginStatusDTO.Message = "Your current password does not match.";
        //            LoginStatusDTO.MessageStatus = "Fail";
        //            return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //        }
        //    }
        //    else
        //    {
        //        LoginStatusDTO.Message = "The Email supplied was not found.";
        //        LoginStatusDTO.MessageStatus = "Fail";
        //    }
        //    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);


        //}

        ///// <summary>
        ///// User can forget password
        ///// </summary>
        //[HttpGet(nameof(ForgotLoginPassword))]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status400BadRequest)]
        //public async Task<APIResponse> ForgotLoginPassword(string userEmail)
        //{
        //    LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
        //    int statusvalue = 0;
        //    if (!string.IsNullOrEmpty(userEmail))
        //    {
        //        statusvalue = await _login.CheckUserEmailExist(userEmail);
        //        if (statusvalue == 1)
        //        {
        //            //Checking for email count
        //            int emailCount = 0;
        //            emailCount= _login.GetEmailCount(userEmail);
        //            if(emailCount==0)
        //            {
        //                LoginStatusDTO.Message = "No Email address found for this user name, please contact your Administrator to reset your password";
        //                LoginStatusDTO.MessageStatus = "Fail";
        //                return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //            }
        //            else
        //            {
        //                statusvalue = await _login.ForgotPassword(userEmail);
        //                if(statusvalue==1)
        //                {
        //                    LoginStatusDTO.Message = "Your password has been reset and your new password has been send to your email";
        //                    LoginStatusDTO.MessageStatus = "Success";
        //                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //                }
        //                else if (statusvalue == 0)
        //                {
        //                    LoginStatusDTO.Message = "No Email address found for this user name, please contact your personal rep to reset your password";
        //                    LoginStatusDTO.MessageStatus = "Success";
        //                    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //                }
        //            }
        //        }
        //        else
        //        {
        //                LoginStatusDTO.Message = "The Email supplied was not found";
        //                LoginStatusDTO.MessageStatus = "Fail";
        //        }
        //    }
        //    else
        //    {
        //        LoginStatusDTO.Message = "Email is Required";
        //        LoginStatusDTO.MessageStatus = "Fail";
        //    }
        //    return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), LoginStatusDTO);
        //}
        //#endregion


        //[HttpPost(nameof(ClientLogin))]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status400BadRequest)]
        //[ProducesResponseType(StatusCodes.Status404NotFound)]
        //public ActionResult<APIResponse> ClientLogin(ClientLoginDto ClientLoginDto)
        //{
        //    int returvalue;
        //    LoginStatusDTO LoginStatusDTO = new LoginStatusDTO();
        //    if (ClientLoginDto == null)
        //        return BadRequest("Invalid data for this operation");
        //    returvalue = _login.ClientLogin(ClientLoginDto);
        //    if (returvalue == 1)
        //    {
        //        //LoginStatusDTO.Message = "Login Sucessfull";
        //        //LoginStatusDTO.MessageStatus = "Success";
        //        return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), 1);
        //    }
        //    else if(returvalue == 3)
        //    {
        //        //LoginStatusDTO.Message = "The Email supplied was not found";
        //        //LoginStatusDTO.MessageStatus = "Fail";
        //        return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), 3);
        //    }
        //    else if (returvalue == 0)
        //    {
        //        //LoginStatusDTO.Message = "The Password supplied was not found";
        //        //LoginStatusDTO.MessageStatus = "Fail";
        //        return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), 0);
        //    }
        //    else if (returvalue == 2)
        //    {
        //        //LoginStatusDTO.Message = "The Store Code supplied was not found";
        //        //LoginStatusDTO.MessageStatus = "Fail";
        //        return new APIResponse(StatusCodes.Status200OK, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), 2);
        //    }
        //    return new APIResponse(StatusCodes.Status500InternalServerError, Common.GetEnumDescription((StatusCodeMessages)StatusCodes.Status200OK), -1);
        //}



        //[HttpGet(nameof(CheckIsPasswordChangedByclient))]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status400BadRequest)]
        //[ProducesResponseType(StatusCodes.Status404NotFound)]
        //public ActionResult<bool> CheckIsPasswordChangedByclient([FromQuery] string eMail)
        //{
        //    bool status = _login.CheckIsPasswordChangedByclient(eMail);
        //    return status;
        //}
    }
}