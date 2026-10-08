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
        private readonly ILoginService _login;

        public LoginController(ILoginService login)
        {
            _login = login;
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
            {
                return BadRequest("Invalid data for this operation");
            }

            var insertTenantResult = await _login.Register(tenantDto);
            return StatusCode((int)insertTenantResult.StatusCode, insertTenantResult.ResultSet);
        }

        /// <summary>
        /// Authenticate a tenant or store user
        /// </summary>
        [HttpPost(nameof(TenantLogin))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> TenantLogin([FromBody] TenantLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.EMail) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Invalid data for this operation");
            }

            var loginResult = await _login.TenantLogin(request.EMail, request.Password, request.StoreCode);
            return StatusCode((int)loginResult.StatusCode, loginResult.ResultSet);
        }

        /// <summary>
        /// Check Tenant Email Exist or not
        /// </summary>
        [HttpGet(nameof(TenantprofileDetails))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> TenantprofileDetails(string eMail)
        {
            var loginResult = await _login.TenantProfileDetails(eMail);
            return StatusCode((int)loginResult.StatusCode, loginResult.ResultSet);
        }

        /// <summary>
        /// ChangePassword 
        /// </summary>
        [HttpGet("changePassword")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> ChangePassword(string eMail, string oldPassword, string newPassword)
        {
            var changePasswordResult = await _login.ChangePassword(eMail, oldPassword, newPassword);
            return StatusCode((int)changePasswordResult.StatusCode, changePasswordResult.ResultSet);
        }
       
         /// <summary>
        /// forgotPassword 
        /// </summary>
        [HttpGet("forgotPassword")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]

        public async Task<ActionResult> ForgotPassword(string eMail)
        {
            var forgotPasswordResult = await _login.ForgotPassword(eMail);
            return StatusCode((int)forgotPasswordResult.StatusCode, forgotPasswordResult.ResultSet);
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
            var checkTenantEmailResult = await _login.CheckTenantEmail(eMail);
            return StatusCode((int)checkTenantEmailResult.StatusCode, checkTenantEmailResult.ResultSet);
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
            var getTenantStoreDetailsResult = await _login.GetTenantStoreDetails(eMail);
            return StatusCode((int)getTenantStoreDetailsResult.StatusCode, getTenantStoreDetailsResult.ResultSet);
        }

         /// <summary>
        /// CheckIsPasswordChangedBytenant 
        /// </summary>
        [HttpGet("CheckIsPasswordChangedBytenant")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> CheckIsPasswordChangedByTenant(string eMail)
        {
            var passwordChangedByTenantStatus = await _login.CheckIsPasswordChangedByTenant(eMail);
            return StatusCode((int)passwordChangedByTenantStatus.StatusCode, passwordChangedByTenantStatus.ResultSet);
        }

         /// <summary>
        /// change password 
        /// </summary>
        [HttpPost("changepassword")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> ChangePasswordForTenant([FromBody] ChangePasswordDto changePasswordDto)
        {
            var passwordChangedByTenantStatus = await _login.ChangePasswordForTenant(changePasswordDto.Email, changePasswordDto.NewPassword, changePasswordDto.OldPassword);
            return StatusCode((int)passwordChangedByTenantStatus.StatusCode, passwordChangedByTenantStatus.ResultSet);
        }

           /// <summary>
        /// change password 
        /// </summary>
        [HttpPost("forgotpassword")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Result<LoginIoResponse>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Result<object>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(Result<object>))]
        public async Task<ActionResult> ForgotPasswordForTenant([FromBody] ChangePasswordDto changePasswordDto)
        {
            var forgotPasswordStatus = await _login.ForgotPasswordForTenant(changePasswordDto.Email);
            return StatusCode((int)forgotPasswordStatus.StatusCode, forgotPasswordStatus.ResultSet);
        }

        /// <summary>
        /// create store user
        /// </summary>
        [HttpPost(nameof(CreateStoreUser))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> CreateStoreUser([FromBody] StoreUserDto model)
        {
            var result = await _login.CreateStoreUser(model);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// create tenant store
        /// </summary>
        [HttpPost(nameof(CreateTenantStore))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> CreateTenantStore([FromBody] CreateTenantStoreDto model)
        {
            if (model == null)
            {
                return BadRequest("Invalid data for this operation");
            }

            var result = await _login.CreateTenantStore(model.TenantName, model.StoreCode);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// get store users
        /// </summary>
        [HttpGet(nameof(GetStoreUsers))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> GetStoreUsers(string tenantEmail, string storeCode)
        {
            var result = await _login.GetStoreUsers(tenantEmail, storeCode);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// activate or deactivate store user
        /// </summary>
        [HttpPost(nameof(SetStoreUserActiveStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SetStoreUserActiveStatus(string tenantEmail, string storeCode, string userEmail, bool isActive)
        {
            var result = await _login.SetStoreUserActiveStatus(tenantEmail, storeCode, userEmail, isActive);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// get tenant store activation statuses
        /// </summary>
        [HttpGet(nameof(GetTenantStoreStatuses))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> GetTenantStoreStatuses(string tenantEmail)
        {
            var result = await _login.GetTenantStoreStatuses(tenantEmail);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// activate or deactivate a tenant store
        /// </summary>
        [HttpPost(nameof(SetTenantStoreActiveStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SetTenantStoreActiveStatus(string tenantEmail, string storeCode, bool isActive, string activatedBy)
        {
            var result = await _login.SetTenantStoreActiveStatus(tenantEmail, storeCode, isActive, activatedBy);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// get store activation statuses across all tenants (super admin)
        /// </summary>
        [HttpGet(nameof(GetAllStoreStatuses))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> GetAllStoreStatuses()
        {
            var result = await _login.GetAllStoreStatuses();
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// activate or deactivate a store by store code (super admin)
        /// </summary>
        [HttpPost(nameof(SetStoreActiveStatusByStoreCode))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SetStoreActiveStatusByStoreCode(string storeCode, bool isActive, string activatedBy)
        {
            var result = await _login.SetStoreActiveStatusByStoreCode(storeCode, isActive, activatedBy);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// get approval state of all tenants (super admin)
        /// </summary>
        [HttpGet(nameof(GetAllTenantApprovals))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> GetAllTenantApprovals()
        {
            var result = await _login.GetAllTenantApprovals();
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// approve or reject a tenant (super admin)
        /// </summary>
        [HttpPost(nameof(SetTenantApprovalStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> SetTenantApprovalStatus(string tenantId, string approvalStatus, string reason, string actionedBy)
        {
            var result = await _login.SetTenantApprovalStatus(tenantId, approvalStatus, reason, actionedBy);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// get store configuration
        /// </summary>
        [HttpGet("GetStoreConfiguration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> GetStoreConfiguration(string tenantEmail, string storeCode)
        {
            var result = await _login.GetStoreConfiguration(tenantEmail, storeCode);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }

        /// <summary>
        /// create or update store configuration
        /// </summary>
        [HttpPost("NewStoreConfiguration")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> NewStoreConfiguration([FromBody] NewStoreConfigurationDto model)
        {
            var result = await _login.NewStoreConfiguration(model);
            return StatusCode((int)result.StatusCode, result.ResultSet);
        }
    }
}