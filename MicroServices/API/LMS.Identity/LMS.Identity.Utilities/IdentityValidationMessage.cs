using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.Utilities
{
    public class IdentityValidationMessage
    {
        public const string IDENTITY_INSERT_SUCCESS_MESSAGE = "Registration successful.";
        public const string IDENTITY_INSERT_ERROR_MESSAGE = "Error while inserting tenant.";
        public const string IDENTITY_LOGINTENANT_ERROR_MESSAGE = "Error while tenant login.";
        public const string IDENTITY_DATANOTFOUND_ERROR_MESSAGE = "Tenant info not found while inserting.";
        public const string IDENTITY_DATAFOUND_ERROR_MESSAGE = "Tenant already registered.";
        public const string IDENTITY_DATAFOUND_LOGIN_MESSAGE = "Tenant login successful.";
        public const string IDENTITY_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE = "Password already changed successfully.";
        public const string IDENTITY_DATAFOUND_PASSWORDCHANGE_MESSAGE = "Need to change password.";
        public const string IDENTITY_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE = "Password sent to registered email successfully.";
        public const string IDENTITY_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE = "Password changed successfully.";
        public const string IDENTITY_DATANOTFOUND_LOGIN_MESSAGE = "Tenant login not found.";
        public const string IDENTITY_DATANOTFOUND_EMAIL_MESSAGE = "Email supplied was not found.";
        public const string IDENTITY_DATAFOUNDCHANGEPASSWORDL_MESSAGE = "Password changed successfully.";
        public const string IDENTITY_DATAFOUND_EMAIL_MESSAGE = "Email supplied was found.";
        public const string IDENTITY_TENANT_NOT_FOUND = "Tenant information is missing in the request.";
        public const string IDENTITY_TENANTPASSWORDDATA_NOT_FOUND = "Tenant password information is missing in the request.";
    }
}
