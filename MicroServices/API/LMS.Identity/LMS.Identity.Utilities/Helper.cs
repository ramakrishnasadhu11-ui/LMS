using System;
using System.Net;

namespace LMS.Identity.Utilities
{
   public class ActionReturnType
    {
        public HttpStatusCode StatusCode { get; set; }
        public string XTotalCount { get; set; }
        public object ResultSet { get; set; }
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
}
