using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.Utilities
{
    public class IdentityValidationMessage
    {
        public const string IDENTITY_INSERT_SUCCESS_MESSAGE = "Registration successfully";
        public const string IDENTITY_INSERT_ERROR_MESSAGE = "Error while tenant inserting";
        public const string IDENTITY_LOGINTENANT_ERROR_MESSAGE = "Error while tenant login";
        public const string IDENTITY_DATANOTFOUND_ERROR_MESSAGE = "Tenant Info not found while inserting";
        public const string IDENTITY_DATAFOUND_ERROR_MESSAGE = "Tenant Already Registered";
        public const string IDENTITY_DATAFOUND_LOGIN_MESSAGE = "Tenant Login Successfully";
        public const string IDENTITY_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE = "Password Already Changed Successfully";
        public const string IDENTITY_DATAFOUND_PASSWORDCHANGE_MESSAGE = "Need to change Password";
        public const string IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE = "Password Changed Successfully";
        public const string IDENTITY_DATANOTFOUND_LOGIN_MESSAGE = "Tenant Login Not Found";
        public const string IDENTITY_TENANT_NOT_FOUND = "Tenant Information is Missing in the Request";
        public const string IDENTITY_TENANTPASSWORDDATA_NOT_FOUND = "Tenant Password Information is Missing in the Request";
    }
}
