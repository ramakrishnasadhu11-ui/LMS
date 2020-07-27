using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Caching;
using System.Text;

namespace LMS.Identity.Utilities
{
  
    
    public class ActionReturnType
    {
        public HttpStatusCode StatusCode { get; set; }
        public string XTotalCount { get; set; }
        public object ResultSet { get; set; }
    }

    public interface IResult
    {
        List<Error> Error { get; set; }
    }
    public class Result<T> : IResult
    {
        private readonly Error _error;
        public Result(Exception ex)
        {
            var errMsg = ex.InnerException != null ? ex.InnerException.InnerException != null ? ex.InnerException.InnerException.Message : ex.InnerException.Message : ex.Message;
            _error = new Error(ValidationCategory.ERROR, ValidationCode.EXCEPTION, errMsg, "N/A");
            this.Error = new List<Error>() { _error };
        }
        public Result()
        {
            _error = new Error(ValidationCategory.NOERROR, ValidationCode.NO_ERROR, "No Error !", "N/A");
            this.Error = new List<Error> { _error };
        }

        public List<Error> Error { get; set; }
    }

    public class Error
    {
        public Error(ValidationCategory category, ValidationCode code, string message, string field)
        {
            this.ValidationCategory = category.ToString();
            this.ValidationCode = code.ToString();
            this.Message = message;
            this.Field = field;
        }
        /// <summary>
        /// Type of Validation
        /// <example>
        /// example : VALIDATION, ERROR
        /// </example>
        /// </summary>
        [JsonProperty("category")]
        public string ValidationCategory { get; set; }
        /// <summary>
        /// Validation Code
        /// <example>
        /// example : INVALID_TYPE, INVALID_ID, MISSING_TYPE, NOT_FOUND, IS_REQUIRED, SYSTEM_VALIDATION, MAX_LENGTH, MIN_LENGTH, EXCEPTION, INCORRECT_TYPE 
        /// </example>
        /// </summary>
        [JsonProperty("code")]
        public string ValidationCode { get; set; }
        /// <summary>
        /// Description of Validation
        /// </summary>
        [JsonProperty("detail")]
        public string Message { get; set; }
        /// <summary>
        /// Validation field
        /// </summary>
        [JsonProperty("field")]
        public string Field { get; set; }

    }

    public enum ValidationCategory
    {
        NOERROR = 0,
        VALIDATION = 1,
        ERROR = 2
    }
    public enum ValidationCode
    {
        NO_ERROR = 0,
        INVALID_TYPE = 1,
        INVALID_ID = 2,
        MISSING_TYPE = 3,
        NOT_FOUND = 4,
        IS_REQUIRED = 5,
        SYSTEM_VALIDATION = 6,
        MAX_LENGTH = 7,
        EXCEPTION = 8,
        MIN_LENGTH = 9,
        INCORRECT_TYPE = 10,
        UN_AUTHORIZED = 11,
        INVALID_DATA = 12,
        DATABASE = 13
    }
    public enum ValidationType
    {
        DATA_TYPE = 1,
        NOT_FOUND = 2,
        REQUIRED_FIELD = 3,
        FIELD = 4,
        MIN_LENGTH = 5,
        MAX_LENGTH = 6,
        REQUIRED_INPUT = 7,
        NO_DATA_FOUND = 8,
        SYSTEM = 9,
        IN_CORRECT = 10,
        INVALID_DATA = 11,
        CUSTOM_VALIDATION = 12,
        DATABASE = 13
    }

    public enum APIModule
    {
        Account = 1,
        Activity,
        Alert,
        Appointment,
        Assessment,
        Audit,
        Authorization,
        CarePlan,
        CareTeam,
        ConsentForm,
        Document,
        Encounter,
        Eligibility,
        Letter,
        Master,
        Medication,
        Member,
        Payor,
        Profile,
        Provider,
        Risk,
        ServicePlan,
        IntegrationAPI
    }

    public enum UrlPathName
    {
        Authorization,
        ServicePlan
    }
    public static class ActionSet
    {
        public static ActionReturnType ActionReturnType(HttpStatusCode statusCode, object resultSet = null, int xTotalCount = 0)
        {
            var actionResult = new ActionReturnType
            {
                StatusCode = statusCode,
                ResultSet = resultSet,
                XTotalCount = Convert.ToString(xTotalCount)
            };
            return actionResult;
        }
    }

    public class APIManagerKey
    {
        public string Name { get; set; }
        public string Version { get; set; }
    }
   
}
