using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Master.Utilities
{
    public class CustomerValidationMessage
    {
        public const string CUSTOMER_INSERT_SUCCESS_MESSAGE = "Customer Added successfully";
        public const string CUSTOMER_INSERT_ERROR_MESSAGE = "Error while Customer inserting";
        public const string MASTER_LOGINTENANT_ERROR_MESSAGE = "Error while tenant login";
        public const string CUSTOMER_DATANOTFOUND_ERROR_MESSAGE = "Customer Info not found while inserting";
        public const string CUSTOMER_DATAFOUND_ERROR_MESSAGE = "Customer Already Inserted";
        public const string MASTER_DATAFOUND_LOGIN_MESSAGE = "Tenant Login Successfully";
        public const string MASTER_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE = "Password Already Changed Successfully";
        public const string MASTER_DATAFOUND_PASSWORDCHANGE_MESSAGE = "Need to change Password";
        public const string MASTER_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE = "Password Sent to Registered Email Successfully";
        public const string MASTER_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE = "Password Changed Successfully";
        public const string MASTER_DATANOTFOUND_LOGIN_MESSAGE = "Tenant Login Not Found";
        public const string MASTER_DATANOTFOUND_EMAIL_MESSAGE = "Email Suppplied was Not Found";
        public const string MASTER_DATAFOUNDCHANGEPASSWORDL_MESSAGE = "Password Changed Successfully";
        public const string MASTER_DATAFOUND_EMAIL_MESSAGE = "Email Suppplied was Found";
        public const string MASTER_TENANT_NOT_FOUND = "Tenant Information is Missing in the Request";
        public const string MASTER_TENANTPASSWORDDATA_NOT_FOUND = "Tenant Password Information is Missing in the Request";
    }
}
