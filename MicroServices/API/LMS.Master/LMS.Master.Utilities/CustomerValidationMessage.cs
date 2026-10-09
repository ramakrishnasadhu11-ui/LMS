using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Master.Utilities
{
    public class CustomerValidationMessage
    {
        public const string CUSTOMER_INSERT_SUCCESS_MESSAGE = "Customer added successfully.";
        public const string CUSTOMER_INSERT_ERROR_MESSAGE = "Error while inserting customer.";
        public const string CUSTOMER_UPDATE_SUCCESS_MESSAGE = "Customer updated successfully.";
        public const string CUSTOMER_DELETE_SUCCESS_MESSAGE = "Customer deleted successfully.";
        public const string CUSTOMER_NAME_REQUIRED_MESSAGE = "Customer name is required.";
        public const string CUSTOMER_ADDRESS_REQUIRED_MESSAGE = "Customer address is required.";
        public const string CUSTOMER_PHONE_INVALID_MESSAGE = "Please enter a valid phone number (7-15 digits).";
        public const string CUSTOMER_EMAIL_INVALID_MESSAGE = "Please enter a valid email address.";
        public const string MASTER_LOGINTENANT_ERROR_MESSAGE = "Error while tenant login.";
        public const string CUSTOMER_DATANOTFOUND_ERROR_MESSAGE = "Customer info not found while inserting.";
        public const string CUSTOMER_DATAFOUND_ERROR_MESSAGE = "Customer already inserted.";
        public const string MASTER_DATAFOUND_LOGIN_MESSAGE = "Tenant login successful.";
        public const string MASTER_DATAFOUND_PASSWORDALREADYCHANGED_MESSAGE = "Password already changed successfully.";
        public const string MASTER_DATAFOUND_PASSWORDCHANGE_MESSAGE = "Need to change password.";
        public const string MASTER_DATAFOUND_CHANGEPASSWORDSUCCESS_MESSAGE = "Password sent to registered email successfully.";
        public const string MASTER_DATAFOUND_PASSWORDCHANGESUCCESS_MESSAGE = "Password changed successfully.";
        public const string MASTER_DATANOTFOUND_LOGIN_MESSAGE = "Tenant login not found.";
        public const string MASTER_DATANOTFOUND_EMAIL_MESSAGE = "Email supplied was not found.";
        public const string MASTER_DATAFOUNDCHANGEPASSWORDL_MESSAGE = "Password changed successfully.";
        public const string MASTER_DATAFOUND_EMAIL_MESSAGE = "Email supplied was found.";
        public const string MASTER_TENANT_NOT_FOUND = "Tenant information is missing in the request.";
        public const string MASTER_TENANTPASSWORDDATA_NOT_FOUND = "Tenant password information is missing in the request.";
    }
}
