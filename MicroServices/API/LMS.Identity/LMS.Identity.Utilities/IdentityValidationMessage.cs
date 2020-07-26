using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Identity.Utilities
{
    public class IdentityValidationMessage
    {
        public const string IDENTITY_INSERT_SUCCESS_MESSAGE = "Tenant inserted successfully";
        public const string IDENTITY_INSERT_ERROR_MESSAGE = "Error while tenant inserting";
        public const string IDENTITY_DATANOTFOUND_ERROR_MESSAGE = "Tenant Info not found while inserting";
        public const string IDENTITY_DATAFOUND_ERROR_MESSAGE = "Tenant Already Registered";
    }
}
