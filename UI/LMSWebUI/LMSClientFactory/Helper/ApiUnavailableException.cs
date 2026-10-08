using System;

namespace LMSClientFactory.Helper
{
    /// <summary>
    /// Thrown when the downstream API could not be reached at all (connection refused,
    /// DNS failure, timeout, 500, gateway timeout). This is deliberately distinct from a
    /// 404/business failure so callers can tell "service is down" apart from
    /// "the request was understood and rejected".
    /// </summary>
    public class ApiUnavailableException : Exception
    {
        public int? StatusCode { get; }

        /// <summary>
        /// Raw response body from the downstream API, when an HTTP response was received.
        /// Do not expose this value directly to end users; it may contain sensitive details.
        /// </summary>
        public string ResponseContent { get; } = string.Empty;

        public ApiUnavailableException(string message)
            : base(message)
        {
        }

        public ApiUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public ApiUnavailableException(string message, Exception innerException, int statusCode, string responseContent)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseContent = responseContent ?? string.Empty;
        }
    }
}
