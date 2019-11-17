using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace LMS.Identity.WebApi.Utility
{
    public enum StatusCodeMessages
    {
        Unknown = 0,
        [Description("Data found.")]
         Datafound = 200,

        [Description("No Data Found.")]
        NoDataFound = 400,

        [Description("Record Already Exists.")]
        RecordAlreadyExists = 422,

        [Description("Record Not Modified.")]
        RecordNotModified = 304,

        [Description("Error Occured while Processing Request.")]
        ErrorOccuredwhileProcessingRequest = 500
    }
}
