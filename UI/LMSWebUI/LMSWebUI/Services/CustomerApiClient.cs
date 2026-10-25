using LMSClientFactory.Helper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace LMSWebUI.Services
{
    public class CustomerApiClient : HttpClientApi, ICustomerApiClient
    {
        public CustomerApiClient(IOptions<ServiceEndpointsOptions> options, IHttpContextAccessor httpContextAccessor)
            : base(options.Value.Master.BaseUrl.TrimEnd('/') + "/Customer", ResolveAccessToken(httpContextAccessor))
        {
        }

        private static string ResolveAccessToken(IHttpContextAccessor httpContextAccessor)
        {
            var httpContext = httpContextAccessor?.HttpContext;
            if (httpContext == null)
            {
                return string.Empty;
            }

            var sessionToken = httpContext.Session?.GetString("AccessToken");
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                return sessionToken.Trim();
            }

            var authHeader = httpContext.Request?.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                const string bearerPrefix = "Bearer ";
                if (authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader.Substring(bearerPrefix.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }
            }

            return string.Empty;
        }
    }
}
